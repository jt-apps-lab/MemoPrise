using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace MemoPrise;
public record DailyDose(string Time, string Dose);
public record TreatmentPeriod(DateTime Start, DateTime? End, List<DailyDose> Prises);
// Dose/Times are retained to read treatments created by version 1.0.
public record Treatment(string Id, string Name, string Dose, string Times, int Days, DateTime Start, DateTime? End, string Note, bool Active = true, List<DailyDose>? Prises = null, List<TreatmentPeriod>? Periods = null);
public record Intake(string Key, string TreatmentId, string Name, string Dose, string Note, DateTime Due, string Status = "pending", DateTime? Taken = null, DateTime? Snooze = null);
public record BackupData(int Version, List<Treatment> Treatments, List<Intake> Intakes, Dictionary<string,string> Settings);
public static class Schedule
{
    public static List<DailyDose> Doses(Treatment t) => t.Periods?.FirstOrDefault()?.Prises ?? t.Prises ?? t.Times.Split(';').Select(time=>new DailyDose(time,t.Dose)).ToList();
    public static List<DailyDose> Doses(Treatment t,DateTime day) => t.Periods==null?Doses(t):t.Periods.FirstOrDefault(p=>day.Date>=p.Start.Date && (p.End==null || day.Date<=p.End.Value.Date))?.Prises ?? new List<DailyDose>();
    public static (TreatmentPeriod Previous,TreatmentPeriod Current)? PosologyChange(Treatment t,DateTime day)
    {
        if(!Applies(t,day) || t.Periods==null) return null;
        int index=t.Periods.FindIndex(p=>day.Date>=p.Start.Date && (p.End==null || day.Date<=p.End.Value.Date));
        if(index<=0) return null;
        var current=t.Periods[index]; var previous=t.Periods[index-1];
        var first=current.Start.Date;
        while(first<day.Date && (t.Days & (1<<(int)first.DayOfWeek))==0) first=first.AddDays(1);
        if(first!=day.Date || previous.Prises.OrderBy(p=>p.Time).SequenceEqual(current.Prises.OrderBy(p=>p.Time))) return null;
        return (previous,current);
    }
    public static void ValidatePeriods(Treatment t)
    {
        if(t.Periods==null) {ValidateDoses(Doses(t)); return;}
        if(t.Periods.Count<1) throw new InvalidDataException("Ajoutez au moins une période.");
        for(int n=0;n<t.Periods.Count;n++)
        {
            var p=t.Periods[n]; ValidateDoses(p.Prises);
            if(p.Start.TimeOfDay!=TimeSpan.Zero || (p.End!=null && p.End.Value.TimeOfDay!=TimeSpan.Zero) || p.End<p.Start) throw new InvalidDataException($"Dates invalides pour la période {n+1}.");
            if(n==0 && p.Start!=t.Start.Date) throw new InvalidDataException("La première période doit commencer à la date de début du traitement.");
            if(n>0)
            {
                var prior=t.Periods[n-1];
                if(prior.End==null || prior.End.Value.Date==DateTime.MaxValue.Date) throw new InvalidDataException("Seule la dernière période peut être sans date de fin.");
                var expected=prior.End.Value.AddDays(1);
                if(p.Start!=expected) throw new InvalidDataException($"La période {n+1} doit commencer le {expected:dd/MM/yyyy}, après la précédente.");
            }
        }
        if(t.Periods[^1].End?.Date!=t.End?.Date) throw new InvalidDataException("La dernière période doit correspondre à la fin du traitement.");
    }
    public static List<DailyDose> ValidateDoses(IEnumerable<DailyDose> entries)
    {
        var list=entries.ToList();
        if(list.Count<1 || list.Count>24) throw new InvalidDataException("Choisissez entre 1 et 24 prises par jour.");
        foreach(var entry in list)
        {
            if(string.IsNullOrWhiteSpace(entry.Dose)) throw new InvalidDataException("Indiquez la quantité pour chaque prise.");
            if(!TimeSpan.TryParseExact(entry.Time,@"hh\:mm",CultureInfo.InvariantCulture,out var time) || time.TotalHours>=24) throw new InvalidDataException("Les horaires doivent être compris entre 00:00 et 23:59.");
        }
        if(list.Select(p=>p.Time).Distinct().Count()!=list.Count) throw new InvalidDataException("Deux prises ont le même horaire. Choisissez des horaires différents.");
        return list.OrderBy(p=>p.Time,StringComparer.Ordinal).ToList();
    }
    public static bool Applies(Treatment t, DateTime day) => t.Active && day.Date >= t.Start.Date && (t.End == null || day.Date <= t.End.Value.Date) && (t.Days & (1 << (int)day.DayOfWeek)) != 0;
    public static IEnumerable<Intake> ForDay(Treatment t, DateTime day)
    {
        if (!Applies(t, day)) yield break;
        foreach (var entry in Doses(t,day))
        {
            var due = day.Date + TimeSpan.ParseExact(entry.Time, @"hh\:mm", CultureInfo.InvariantCulture);
            yield return new Intake(t.Id + ":" + due.ToString("yyyyMMddHHmm"), t.Id, t.Name, entry.Dose, t.Note, due);
        }
    }
    public static bool NeedsReminder(Intake i, DateTime now) => i.Status == "pending" && i.Due <= now && (i.Snooze == null || i.Snooze <= now);
}
public sealed class Store : IDisposable
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoPrise");
    private readonly SqliteConnection db;
    public Store(string? path = null)
    {
        Directory.CreateDirectory(Folder);
        db = new SqliteConnection("Data Source=" + (path ?? Path.Combine(Folder, "memoprise.db"))); db.Open();
        Run("CREATE TABLE IF NOT EXISTS treatments(id TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS intakes(id TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(id TEXT PRIMARY KEY,value TEXT NOT NULL)");
    }
    void Run(string sql, params (string, object)[] values) { using var c = db.CreateCommand(); c.CommandText = sql; foreach (var v in values) c.Parameters.AddWithValue(v.Item1, v.Item2); c.ExecuteNonQuery(); }
    List<T> Read<T>(string table) { using var c = db.CreateCommand(); c.CommandText = "SELECT json FROM " + table; using var r = c.ExecuteReader(); var list = new List<T>(); while(r.Read()) list.Add(JsonSerializer.Deserialize<T>(r.GetString(0))!); return list; }
    public List<Treatment> Treatments() => Read<Treatment>("treatments");
    public List<Intake> Intakes() => Read<Intake>("intakes");
    public void Save(Treatment t) => Run("INSERT OR REPLACE INTO treatments VALUES($id,$json)", ("$id",t.Id),("$json",JsonSerializer.Serialize(t)));
    public void Save(Intake i) => Run("INSERT OR REPLACE INTO intakes VALUES($id,$json)", ("$id",i.Key),("$json",JsonSerializer.Serialize(i)));
    public string Setting(string key, string fallback) { using var c = db.CreateCommand(); c.CommandText = "SELECT value FROM settings WHERE id=$id"; c.Parameters.AddWithValue("$id",key); return c.ExecuteScalar() as string ?? fallback; }
    public void Setting(string key, string value, bool write) => Run("INSERT OR REPLACE INTO settings VALUES($id,$value)",("$id",key),("$value",value));
    public List<Intake> Day(DateTime day)
    {
        var saved = Intakes().Where(i => i.Due.Date == day.Date).ToDictionary(i=>i.Key);
        foreach (var t in Treatments())
        {
            var effective = DateTime.Parse(Setting("effective:"+t.Id,t.Start.ToString("O")),CultureInfo.InvariantCulture);
            foreach (var i in Schedule.ForDay(t,day)) if(i.Due >= effective && !saved.ContainsKey(i.Key)) { Save(i); saved.Add(i.Key,i); }
        }
        return saved.Values.OrderBy(i=>i.Due).ThenBy(i=>i.Name).ToList();
    }
    public void MaterializePast()
    {
        var earliest = Treatments().Select(t=>t.Start.Date).DefaultIfEmpty(DateTime.Today).Min();
        var last = DateTime.Parse(Setting("materialized", earliest.AddDays(-1).ToString("O")), CultureInfo.InvariantCulture).Date;
        for(var day = last.AddDays(1); day <= DateTime.Today; day = day.AddDays(1)) Day(day);
        Setting("materialized",DateTime.Today.ToString("O"),true);
    }
    public void ReplaceFuture(string id)
    {
        foreach(var i in Intakes().Where(i=>i.TreatmentId==id && i.Due > DateTime.Now && i.Status=="pending")) Run("DELETE FROM intakes WHERE id=$id",("$id",i.Key));
    }
    public void DeleteTreatment(string id)
    {
        MaterializePast();
        using var transaction=db.BeginTransaction();
        try {ReplaceFuture(id); Run("DELETE FROM treatments WHERE id=$id",("$id",id)); Run("DELETE FROM settings WHERE id=$id",("$id","effective:"+id)); transaction.Commit();}
        catch {transaction.Rollback(); throw;}
    }
    public void Backup(string path)
    {
        MaterializePast(); var settings = new Dictionary<string,string>();
        using(var c=db.CreateCommand()) { c.CommandText="SELECT id,value FROM settings"; using var r=c.ExecuteReader(); while(r.Read()) settings[r.GetString(0)]=r.GetString(1); }
        File.WriteAllText(path,JsonSerializer.Serialize(new BackupData(3,Treatments(),Intakes(),settings), new JsonSerializerOptions {WriteIndented=true}));
    }
    public void Restore(string path)
    {
        var data = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(path)) ?? throw new InvalidDataException("Sauvegarde vide.");
        if(data.Version<1 || data.Version>3 || data.Treatments==null || data.Intakes==null || data.Settings==null) throw new InvalidDataException("Sauvegarde incompatible.");
        foreach(var t in data.Treatments) { if(string.IsNullOrWhiteSpace(t.Name) || t.Days < 1 || t.Days > 127) throw new InvalidDataException("Traitement invalide."); ValidateTreatment(t); }
        using var transaction=db.BeginTransaction();
        try { Run("DELETE FROM treatments; DELETE FROM intakes; DELETE FROM settings"); foreach(var t in data.Treatments) Save(t); foreach(var i in data.Intakes) Save(i); foreach(var s in data.Settings) Setting(s.Key,s.Value,true); transaction.Commit(); }
        catch { transaction.Rollback(); throw; }
    }
    public void Dispose() => db.Dispose();
    static void ValidateTreatment(Treatment t) => Schedule.ValidatePeriods(t);
}
