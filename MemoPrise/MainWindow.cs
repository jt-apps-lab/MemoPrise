using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Calendar = System.Windows.Controls.Calendar;
namespace MemoPrise;
public partial class MainWindow : Window
{
    readonly Store store;
    readonly StackPanel body = new() {MaxWidth=1120};
    readonly ScrollViewer pageScroll=new() {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(22,28,26,22)};
    string renderedPage="";
    readonly Forms.NotifyIcon tray = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly Dictionary<string,DateTime> shown = new();
    readonly Dictionary<string,Button> navigation=new();
    Window? reminder;
    string page = "Aujourd’hui";
    DateTime selected = DateTime.Today;
    bool quitting;
    static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public MainWindow(bool preview = false)
    {
        store = new Store(preview ? Path.Combine(Path.GetTempPath(), "memoprise-preview-"+Guid.NewGuid()+".db") : null);
        Icon=AppIcon.WindowIcon; Title="MémoPrise · "+typeof(MainWindow).Assembly.GetName().Version?.ToString(3); Width=1080; Height=760; MinWidth=820; MinHeight=600;
        FontFamily=new FontFamily("Segoe UI"); FontSize=18; Foreground=Theme.Text; Background=Theme.Background; WindowStartupLocation=WindowStartupLocation.CenterScreen; UseLayoutRounding=true;
        var root=new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(252)}); root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar=new StackPanel();
        var brand=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,24)};
        brand.Children.Add(new Image {Source=AppIcon.Logo,Width=40,Height=40,Margin=new Thickness(0,0,10,0)});
        brand.Children.Add(new TextBlock {Text="MémoPrise",FontSize=24,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        sidebar.Children.Add(brand);
        foreach(var name in new[]{"Aujourd’hui","Calendrier","Boîte à pharmacie","Paramètres"}) {var button=Button(name,()=> {page=name; Render();}); button.HorizontalAlignment=HorizontalAlignment.Stretch; button.HorizontalContentAlignment=HorizontalAlignment.Left; button.Margin=new Thickness(0,5,0,5); navigation[name]=button; sidebar.Children.Add(button);}
        var actions=new StackPanel(); var reduce=Button("Réduire l’application",()=>WindowState=WindowState.Minimized); reduce.ToolTip="Les rappels restent actifs. Retrouvez MémoPrise dans la barre des tâches."; reduce.Margin=new Thickness(0,4,0,4); System.Windows.Automation.AutomationProperties.SetAutomationId(reduce,"minimize-app"); actions.Children.Add(reduce);
        var quit=Button("Quitter l’application",Quit); quit.Margin=new Thickness(0,4,0,4); actions.Children.Add(quit);
        var dock=new DockPanel(); DockPanel.SetDock(actions,Dock.Bottom); dock.Children.Add(actions); dock.Children.Add(sidebar);
        root.Children.Add(new Border {Background=Theme.Sidebar,CornerRadius=new CornerRadius(22),Margin=new Thickness(12),Padding=new Thickness(18,22,18,18),Child=dock}); pageScroll.Content=body; Grid.SetColumn(pageScroll,1); root.Children.Add(pageScroll); Content=root;
        tray.Icon=AppIcon.CreateTrayIcon(); tray.Text="MémoPrise — rappels actifs"; tray.Visible=true;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Open);
        var menu=new Forms.ContextMenuStrip(); menu.Items.Add("Ouvrir MémoPrise",null,(_,_)=>Dispatcher.Invoke(Open)); menu.Items.Add("Quitter",null,(_,_)=>Dispatcher.Invoke(Quit)); tray.ContextMenuStrip=menu;
        Closing+=(_,e)=> {if(!quitting) {e.Cancel=true; Hide();}};
        store.MaterializePast();
        if(!preview && store.Setting("startup","true")=="true") SetStartup(true);
        ApplyFont(); Render(); timer.Tick+=(_,_)=> {if(!preview) Tick();}; timer.Start();
        Loaded+=(_,_)=> {if(!preview) Tick();};
    }
    static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    internal static TextBlock Text(string value,int size=18,bool bold=false)=>new() {Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
    internal static Button Button(string label,Action action) {var b=new Button {Content=label,HorizontalAlignment=HorizontalAlignment.Left}; b.Click+=(_,_)=>action(); return b;}
    internal static Button Primary(Button button) {button.Background=Theme.Accent; button.Foreground=Brushes.White; button.BorderBrush=Theme.Solid("#527DB5"); return button;}
    static Border Badge(string label,string background="#EDF4FF",string foreground="#355C89")=>new() {Background=Brush(background),CornerRadius=new CornerRadius(8),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,6),HorizontalAlignment=HorizontalAlignment.Left,Child=new TextBlock {Text=label,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=Brush(foreground),TextWrapping=TextWrapping.Wrap}};
    static Border Card(UIElement content)=>new() {Background=Brushes.White,BorderBrush=Theme.Outline,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Effect=Theme.Shadow,Padding=new Thickness(22),Margin=new Thickness(0,0,0,16),Child=content};
    void Open() {Show(); WindowState=WindowState.Normal; Activate();}
    void Quit() {if(MessageBox.Show("Quitter MémoPrise ? Les rappels seront arrêtés jusqu’au prochain lancement.","Quitter",MessageBoxButton.YesNo)==MessageBoxResult.Yes) {quitting=true; timer.Stop(); reminder?.Close(); tray.Dispose(); store.Dispose(); System.Windows.Application.Current.Shutdown();}}
    void Render()
    {
        double offset=renderedPage==page?pageScroll.VerticalOffset:0; renderedPage=page;
        body.Children.Clear(); body.Children.Add(Text(page,32,true));
        foreach(var entry in navigation) {entry.Value.Background=entry.Key==page?Theme.Accent:Theme.Gradient("#F8FBFF","#EAF0FF"); entry.Value.Foreground=entry.Key==page?Brushes.White:Theme.Text;}
        if(page=="Aujourd’hui") Today(); else if(page=="Calendrier") History(); else if(page=="Boîte à pharmacie") Pharmacy(); else Settings();
        pageScroll.UpdateLayout(); pageScroll.ScrollToVerticalOffset(offset);
    }
    void Today()
    {
        body.Children.Add(Text(DateTime.Today.ToString("dddd d MMMM yyyy",French),18));
        foreach(var treatment in store.Treatments()) if(PosologyNotice(treatment,DateTime.Today) is Border notice) body.Children.Add(notice);
        var live=store.Treatments().Select(t=>t.Id).ToHashSet(); var list=store.Day(DateTime.Today).Where(i=>i.Status!="pending" || live.Contains(i.TreatmentId)).ToList(); var next=list.FirstOrDefault(i=>i.Status=="pending" && i.Due>DateTime.Now);
        var overview=new StackPanel(); overview.Children.Add(Text(next==null?"Votre suivi de la journée":$"Prochaines prises à {next.Due:HH:mm}",22,true)); if(next!=null) overview.Children.Add(Text(string.Join(" · ",list.Where(i=>i.Status=="pending" && i.Due.TimeOfDay==next.Due.TimeOfDay).Select(i=>i.Name)),17)); int completed=list.Count(i=>i.Status=="taken"); overview.Children.Add(Text($"{completed} prise{(completed>1?"s":"")} validée{(completed>1?"s":"")} sur {list.Count}",16));
        if(list.Count>0) overview.Children.Add(new ProgressBar {Minimum=0,Maximum=list.Count,Value=completed,Height=6,Foreground=Theme.Solid("#628CBF"),Background=Theme.Solid("#D6E5F7"),BorderThickness=new Thickness(0)});
        var hero=Card(overview); hero.Background=Theme.Hero; body.Children.Add(hero);
        if(list.Count==0) {body.Children.Add(Card(Text("Aucune prise prévue aujourd’hui.\nAjoutez vos médicaments dans la boîte à pharmacie.",20))); body.Children.Add(Button("Ajouter un médicament",()=>Edit(null)));}
        foreach(var group in list.GroupBy(i=>i.Due.TimeOfDay).OrderBy(g=>g.Key)) body.Children.Add(IntakeGroup(group.ToList(),true));
        var old=store.Intakes().Count(i=>i.Due.Date<DateTime.Today && i.Status=="pending");
        if(old>0) body.Children.Add(Text($"{old} prise(s) passée(s) sans confirmation. Consultez le calendrier.",16));
    }
    Border IntakeCard(Intake i,bool actions)
    {
        return Card(IntakeContent(i,actions,true));
    }
    Border IntakeGroup(List<Intake> intakes,bool actions)
    {
        var panel=new StackPanel(); var header=new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto}); header.ColumnDefinitions.Add(new ColumnDefinition());
        var time=Text(intakes[0].Due.ToString("HH:mm"),26,true); time.Margin=new Thickness(0,0,20,0); time.VerticalAlignment=VerticalAlignment.Center; header.Children.Add(time);
        int taken=intakes.Count(i=>i.Status=="taken");
        var summary=Text($"{intakes.Count} médicament{(intakes.Count>1?"s":"")} · {taken} prise{(taken>1?"s":"")} validée{(taken>1?"s":"")} sur {intakes.Count}",16); summary.Margin=new Thickness(0); summary.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(summary,1); header.Children.Add(summary);
        panel.Children.Add(new Border {Background=Theme.Hero,CornerRadius=new CornerRadius(17,17,0,0),Padding=new Thickness(18,12,18,12),Child=header});
        for(int n=0;n<intakes.Count;n++) {
            if(n>0) panel.Children.Add(new Border {Height=1,Background=Theme.Outline,Margin=new Thickness(20,0,20,0)});
            panel.Children.Add(new Border {Padding=new Thickness(18,14,18,12),Child=IntakeContent(intakes[n],actions,false)});
        }
        var section=new Border {Background=Brushes.White,BorderBrush=Theme.Outline,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Effect=Theme.Shadow,Margin=new Thickness(0,0,0,20),Child=panel};
        System.Windows.Automation.AutomationProperties.SetAutomationId(section,$"intake-group-{intakes[0].Due:HHmm}");
        return section;
    }
    StackPanel IntakeContent(Intake i,bool actions,bool showTime)
    {
        var treatment=store.Treatments().FirstOrDefault(t=>t.Id==i.TreatmentId);
        var p=new StackPanel(); var heading=new WrapPanel(); var name=Text(showTime?$"{i.Due:HH:mm}   {i.Name}":i.Name,21,true); name.Margin=new Thickness(0,2,14,6); name.MaxWidth=430; heading.Children.Add(name); var quantity=Badge(i.Dose); ((TextBlock)quantity.Child).FontSize=18; heading.Children.Add(quantity); p.Children.Add(heading); if(i.Note.Length>0) {var note=Text(i.Note,16); note.Margin=new Thickness(0,0,0,7); p.Children.Add(note);}
        if(treatment!=null && PosologyNotice(treatment,i.Due.Date) is Border notice) p.Children.Add(notice);
        string state=i.Status=="taken"?"✓ Pris":i.Status=="skipped"?"Non pris":i.Snooze>DateTime.Now?$"Reporté jusqu’à {i.Snooze:HH:mm}":i.Due<DateTime.Now?(i.Due.Date<DateTime.Today?"Non confirmé":"En retard · à confirmer"):"À prendre";
        var tracking=new WrapPanel(); var status=Badge(state,i.Status=="taken"?"#E7F4EE":"#FFF3E3",i.Status=="taken"?"#217655":"#86501E"); ((TextBlock)status.Child).FontSize=17; tracking.Children.Add(status);
        if(i.Status=="taken" && i.Taken!=null) {var stamp=Text($"Validé le {i.Taken?.ToString("dd/MM à HH:mm",French)}",15); stamp.Margin=new Thickness(2,5,0,6); tracking.Children.Add(stamp);} p.Children.Add(tracking);
        if(actions)
        {
            var row=new WrapPanel();
            if(i.Status=="pending")
            {
                var validate=Primary(Button("Valider la prise",()=>Change(i,"taken"))); System.Windows.Automation.AutomationProperties.SetAutomationId(validate,"validate-"+i.Key); row.Children.Add(validate);
                if(i.Due.Date==DateTime.Today) foreach(int minutes in new[]{15,30,60}) row.Children.Add(Button(minutes==60?"Reporter de 1 h":$"Reporter de {minutes} min",()=>Snooze(i,minutes)));
                var skip=Button("Non pris",()=> {if(MessageBox.Show("Marquer cette prise comme non prise ?","Confirmation",MessageBoxButton.YesNo)==MessageBoxResult.Yes) Change(i,"skipped");}); skip.Background=Brushes.White; row.Children.Add(skip);
            }
            else {var undo=Button("Annuler la validation",()=> {if(MessageBox.Show("Annuler cette validation ?","Confirmation",MessageBoxButton.YesNo)==MessageBoxResult.Yes) Change(i,"pending");}); undo.FontSize=16; undo.Padding=new Thickness(12,6,12,6); undo.Background=Brushes.White; row.Children.Add(undo);}
            p.Children.Add(row);
        }
        return p;
    }
    Border? PosologyNotice(Treatment treatment,DateTime day)
    {
        var change=Schedule.PosologyChange(treatment,day); if(change==null) return null;
        var previous=change.Value.Previous.Prises; var current=change.Value.Current.Prises;
        bool dosageChanged=!previous.Select(p=>p.Dose).OrderBy(d=>d,StringComparer.Ordinal).SequenceEqual(current.Select(p=>p.Dose).OrderBy(d=>d,StringComparer.Ordinal)) || previous.Any(p=>current.Any(c=>c.Time==p.Time && c.Dose!=p.Dose));
        string Describe(List<DailyDose> doses)=>string.Join(" · ",doses.OrderBy(p=>p.Time).Select(p=>$"{p.Dose} à {p.Time}"));
        var content=new StackPanel(); var title=Text((dosageChanged?"Changement de dosage":"Changement de posologie")+" — "+treatment.Name,20,true); title.Foreground=Theme.Solid("#86501E"); content.Children.Add(title);
        content.Children.Add(Text("Avant : "+Describe(previous),16)); content.Children.Add(Text((day.Date==DateTime.Today?"À partir d’aujourd’hui":$"Depuis le {change.Value.Current.Start:dd/MM/yyyy}")+" : "+Describe(current),18,true));
        var notice=Card(content); notice.Background=Theme.Solid("#FFF3E3"); notice.BorderBrush=Theme.Solid("#E3B674");
        System.Windows.Automation.AutomationProperties.SetAutomationId(notice,"posology-change-"+treatment.Id); return notice;
    }
    void Change(Intake i,string state) {store.Save(i with {Status=state,Taken=state=="taken"?DateTime.Now:null,Snooze=null}); shown.Remove(i.Key); Render(); RefreshReminder();}
    void Snooze(Intake i,int minutes) {store.Save(i with {Snooze=DateTime.Now.AddMinutes(minutes)}); shown.Remove(i.Key); Render(); RefreshReminder();}
    void History()
    {
        body.Children.Add(Text("Choisissez une date pour consulter les prises.",17));
        var calendar=new Calendar {SelectedDate=selected,DisplayDate=selected,FirstDayOfWeek=DayOfWeek.Monday,HorizontalAlignment=HorizontalAlignment.Left,LayoutTransform=new ScaleTransform(1.45,1.45),Margin=new Thickness(0,0,0,14)};
        var details=new StackPanel();
        void Fill() {details.Children.Clear(); details.Children.Add(Text(selected.ToString("dddd d MMMM yyyy",French),23,true)); var list=store.Day(selected); if(list.Count==0) details.Children.Add(Text("Aucune prise prévue à cette date.")); foreach(var group in list.GroupBy(i=>i.Due.TimeOfDay).OrderBy(g=>g.Key)) details.Children.Add(IntakeGroup(group.ToList(),selected<=DateTime.Today));}
        calendar.SelectedDatesChanged+=(_,_)=> {if(calendar.SelectedDate is DateTime d) {selected=d.Date; Fill();}};
        calendar.Loaded+=(_,_)=>DecorateCalendar(calendar); calendar.DisplayDateChanged+=(_,_)=>Dispatcher.BeginInvoke(()=>DecorateCalendar(calendar));
        var picker=new StackPanel(); picker.Children.Add(calendar); picker.Children.Add(Text("● Vert : toutes validées\n● Orange : à confirmer\n● Gris : à venir",15));
        picker.Children.Add(Button("Revenir à aujourd’hui",()=> {calendar.DisplayDate=DateTime.Today; calendar.SelectedDate=DateTime.Today;}));
        var pickerCard=Card(picker); pickerCard.Padding=new Thickness(14); pickerCard.MaxWidth=310; pickerCard.HorizontalAlignment=HorizontalAlignment.Left; pickerCard.VerticalAlignment=VerticalAlignment.Top;
        var layout=new Grid(); System.Windows.Automation.AutomationProperties.SetAutomationId(layout,"history-layout"); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); layout.Children.Add(pickerCard); layout.Children.Add(details);
        bool? wasWide=null;
        void ArrangeHistory() {
            bool wide=layout.ActualWidth>=700; if(wasWide==wide) return; wasWide=wide;
            layout.ColumnDefinitions[0].Width=wide?new GridLength(296):new GridLength(1,GridUnitType.Star); layout.ColumnDefinitions[1].Width=new GridLength(wide?20:0); layout.ColumnDefinitions[2].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);
            Grid.SetColumn(details,wide?2:0); Grid.SetRow(details,wide?0:1);
        }
        layout.SizeChanged+=(_,_)=>ArrangeHistory(); body.Children.Add(layout); Fill();
    }
    void DecorateCalendar(DependencyObject node)
    {
        for(int n=0;n<VisualTreeHelper.GetChildrenCount(node);n++)
        {
            var child=VisualTreeHelper.GetChild(node,n);
            if(child is System.Windows.Controls.Primitives.CalendarDayButton b && b.DataContext is DateTime d)
            {
                var list=store.Day(d); b.ToolTip=list.Count==0?"Aucune prise":$"{list.Count(i=>i.Status=="taken")}/{list.Count} prise(s) validée(s)";
                b.Foreground=list.Count==0?Brush("#233B36"):list.All(i=>i.Status=="taken")?Brush("#217655"):d.Date<=DateTime.Today?Brush("#9B591A"):Brush("#687874"); b.FontWeight=list.Count>0?FontWeights.Bold:FontWeights.Normal;
                b.Background=list.Count==0?System.Windows.Media.Brushes.Transparent:list.All(i=>i.Status=="taken")?Brush("#DCF0E3"):d.Date<=DateTime.Today?Brush("#FFF0DA"):Brush("#E8EEEC");
            }
            DecorateCalendar(child);
        }
    }
    void Pharmacy()
    {
        body.Children.Add(Text("Vos traitements et leurs horaires",18)); var add=Primary(Button("+ Ajouter un médicament",()=>Edit(null))); add.Margin=new Thickness(0,4,0,18); body.Children.Add(add);
        var list=store.Treatments().OrderByDescending(t=>t.Active).ThenBy(t=>t.Name).ToList(); if(list.Count==0) body.Children.Add(Card(Text("Votre boîte à pharmacie est vide.\nAjoutez votre premier traitement.",20)));
        foreach(var t in list)
        {
            var p=new StackPanel(); var heading=new DockPanel(); var state=Badge(t.Active?"Actif":"Suspendu",t.Active?"#E7F4EE":"#EEF1F6",t.Active?"#217655":"#63748B"); DockPanel.SetDock(state,Dock.Right); heading.Children.Add(state); heading.Children.Add(Text(t.Name,22,true)); p.Children.Add(heading);
            void Doses(IEnumerable<DailyDose> doses) {var lines=new WrapPanel {Margin=new Thickness(0,0,0,8)}; foreach(var prise in doses) lines.Children.Add(Badge($"{prise.Time} · {prise.Dose}")); p.Children.Add(lines);}
            if(t.Periods!=null) foreach(var period in t.Periods) {p.Children.Add(Text($"Du {period.Start:dd/MM/yyyy}"+(period.End==null?" · puis en continu":$" au {period.End:dd/MM/yyyy}"),16,true)); Doses(period.Prises);}
            else Doses(Schedule.Doses(t));
            p.Children.Add(Text(DayNames(t.Days)+$"\nDu {t.Start:dd/MM/yyyy}"+(t.End==null?" · sans date de fin":$" au {t.End:dd/MM/yyyy}"),16));
            if(t.Note.Length>0) p.Children.Add(Text(t.Note,16)); var row=new WrapPanel(); row.Children.Add(Primary(Button("Modifier",()=>Edit(t)))); row.Children.Add(Button(t.Active?"Suspendre":"Réactiver",()=> {store.MaterializePast(); store.Save(t with {Active=!t.Active}); store.Setting("effective:"+t.Id,DateTime.Now.ToString("O"),true); store.ReplaceFuture(t.Id); Render(); RefreshReminder();}));
            var delete=Button("Supprimer",()=> {if(MessageBox.Show($"Supprimer « {t.Name} » de la boîte à pharmacie ?\n\nSes prochains rappels seront supprimés. L’historique des prises passées sera conservé.","Supprimer le médicament",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes) {store.DeleteTreatment(t.Id); Render(); RefreshReminder();}}); delete.Foreground=Brush("#914D62"); delete.Background=Theme.Gradient("#FFF5F8","#F8E6ED"); delete.ToolTip="Retirer ce médicament en conservant son historique"; row.Children.Add(delete); p.Children.Add(row); body.Children.Add(Card(p));
        }
    }
    static string DayNames(int mask) => mask==127?"Tous les jours":string.Join(", ",Enumerable.Range(0,7).Where(n=>(mask&(1<<n))!=0).Select(n=>French.DateTimeFormat.DayNames[n]));
    void Edit(Treatment? t)
    {
        var dialog=new TreatmentDialog(t) {Owner=this}; if(dialog.ShowDialog()==true && dialog.Result is Treatment result) {store.MaterializePast(); store.Save(result); store.Setting("effective:"+result.Id,DateTime.Now.ToString("O"),true); store.ReplaceFuture(result.Id); page="Boîte à pharmacie"; Render(); RefreshReminder();}
    }
    void ApplyFont() {body.LayoutTransform=new ScaleTransform(store.Setting("large","false")=="true"?1.15:1,store.Setting("large","false")=="true"?1.15:1);}
    void Settings()
    {
        var p=new StackPanel(); p.Children.Add(Text("Rappels et démarrage",22,true));
        var startup=new CheckBox {Content="Lancer avec Windows",IsChecked=store.Setting("startup","true")=="true"}; startup.Click+=(_,_)=> {SetStartup(startup.IsChecked==true); store.Setting("startup",(startup.IsChecked==true).ToString().ToLowerInvariant(),true);}; p.Children.Add(startup);
        var sound=new CheckBox {Content="Signal sonore lors des rappels",IsChecked=store.Setting("sound","true")=="true"}; sound.Click+=(_,_)=>store.Setting("sound",(sound.IsChecked==true).ToString().ToLowerInvariant(),true); p.Children.Add(sound);
        p.Children.Add(Text("Le PC doit être allumé et la session ouverte. Après une veille, les prises du jour non confirmées sont signalées.",16)); body.Children.Add(Card(p));
        var display=new StackPanel(); display.Children.Add(Text("Affichage",22,true));
        var large=new CheckBox {Content="Agrandir les textes et les boutons",IsChecked=store.Setting("large","false")=="true"}; large.Click+=(_,_)=> {store.Setting("large",(large.IsChecked==true).ToString().ToLowerInvariant(),true); ApplyFont();}; display.Children.Add(large); body.Children.Add(Card(display));
        var backup=new StackPanel(); backup.Children.Add(Text("Sauvegarde de vos données",22,true)); backup.Children.Add(Text("Les traitements et l’historique restent sur ce PC. La sauvegarde contient ces informations personnelles.",16)); var actions=new WrapPanel(); backup.Children.Add(actions);
        actions.Children.Add(Primary(Button("Sauvegarder…",()=> {var d=new SaveFileDialog {Filter="Sauvegarde MémoPrise|*.json",FileName=$"MemoPrise-{DateTime.Today:yyyy-MM-dd}.json"}; if(d.ShowDialog()==true) {store.Backup(d.FileName); MessageBox.Show("Sauvegarde enregistrée.");}})));
        actions.Children.Add(Button("Restaurer une sauvegarde…",()=> {var d=new OpenFileDialog {Filter="Sauvegarde MémoPrise|*.json"}; if(d.ShowDialog()==true && MessageBox.Show("Remplacer les données actuelles par cette sauvegarde ? Une copie de sécurité sera conservée.","Restaurer",MessageBoxButton.YesNo)==MessageBoxResult.Yes) {store.Backup(Path.Combine(Store.Folder,$"avant-restauration-{DateTime.Now:yyyyMMdd-HHmmss}.json")); store.Restore(d.FileName); SetStartup(store.Setting("startup","true")=="true"); shown.Clear(); reminder?.Close(); reminder=null; ApplyFont(); Render();}})); body.Children.Add(Card(backup));
    }
    void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled) key.SetValue("MemoPrise","\""+Environment.ProcessPath+"\" --background"); else key.DeleteValue("MemoPrise",false);
    }
    List<Intake> Due()
    {
        var active = store.Treatments().Where(t=>t.Active).Select(t=>t.Id).ToHashSet();
        return store.Day(DateTime.Today).Where(i=>active.Contains(i.TreatmentId) && Schedule.NeedsReminder(i,DateTime.Now)).ToList();
    }
    void Tick()
    {
        store.MaterializePast(); var due=Due();
        if(due.Any(i=>!shown.TryGetValue(i.Key,out var last) || last<DateTime.Now.AddMinutes(-10))) ShowReminder(false);
        if(IsVisible && page=="Aujourd’hui") Render();
    }
    void RefreshReminder() {if(reminder!=null) {reminder.Close(); reminder=null; if(Due().Count>0) ShowReminder(false);}}
    void ShowReminder(bool demo)
    {
        if(reminder!=null) {reminder.Activate(); return;}
        var list=demo?new List<Intake>{new("demo","demo","Exemple de rappel","Quantité prévue sur votre ordonnance","Ceci est un test : aucune prise ne sera enregistrée.",DateTime.Now)}:Due();
        if(list.Count==0) return; foreach(var i in list) shown[i.Key]=DateTime.Now;
        var p=new StackPanel {Margin=new Thickness(24)}; p.Children.Add(Text(demo?"Tester le rappel":"À prendre maintenant :",28,true));
        foreach(var i in list) p.Children.Add(demo?Card(Text(i.Name+"\n"+i.Note,20)):IntakeCard(i,true));
        if(demo) p.Children.Add(Button("Fermer le test",()=>reminder?.Close()));
        else {
            var reports=new WrapPanel();
            foreach(int minutes in new[]{15,30,60}) reports.Children.Add(Button(minutes==60?"Tout reporter de 1 h":$"Tout reporter de {minutes} min",()=> {var until=DateTime.Now.AddMinutes(minutes); foreach(var i in list) {store.Save(i with {Snooze=until}); shown.Remove(i.Key);} reminder?.Close(); Render();}));
            p.Children.Add(reports);
        }
        int changeCount=demo?0:list.Count(i=>store.Treatments().Any(t=>t.Id==i.TreatmentId && Schedule.PosologyChange(t,i.Due.Date)!=null));
        var w=new Window {Icon=AppIcon.WindowIcon,Title="MémoPrise — rappel",Width=660,Height=Math.Min(720,260+list.Count*210+changeCount*200),MinWidth=520,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen,FontFamily=FontFamily,FontSize=18,Background=Background,Content=new ScrollViewer {Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        reminder=w; w.Closed+=(_,_)=> {if(reminder==w) reminder=null;}; w.Show(); w.Activate(); w.Focus();
        if(store.Setting("sound","true")=="true") System.Media.SystemSounds.Exclamation.Play();
    }
    public void CaptureAndExit()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=> {
            void Capture(Window window,string name) {window.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=File.Create(Path.Combine(AppContext.BaseDirectory,name+".png")); encoder.Save(stream);}
            IEnumerable<DependencyObject> Walk(DependencyObject node) {yield return node; for(int n=0;n<VisualTreeHelper.GetChildrenCount(node);n++) foreach(var child in Walk(VisualTreeHelper.GetChild(node,n))) yield return child;}
            T Find<T>(Window window,string id) where T:DependencyObject {window.UpdateLayout(); return Walk(window).OfType<T>().Single(v=>System.Windows.Automation.AutomationProperties.GetAutomationId(v)==id);}
            void ClickIn(Window window,string id) => Find<Button>(window,id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            ClickIn(this,"minimize-app"); if(WindowState!=WindowState.Minimized || !timer.IsEnabled || !tray.Visible) throw new Exception("La réduction doit conserver les rappels."); WindowState=WindowState.Normal;
            Capture(this,"preview-vide");
            var t=new Treatment("preview","Médicament de démonstration","1 cachet","12:00;20:00",127,DateTime.Today.AddDays(-2),null,"Exemple de consigne",true,new List<DailyDose>{new("12:00","1 cachet"),new("20:00","2 cachets")}); store.Save(t); store.MaterializePast();
            var t2=t with {Id="preview-second",Name="Médicament B",Dose="3 gouttes",Note="Avec un verre d’eau",Prises=new(){new("12:00","3 gouttes"),new("20:00","5 gouttes")}}; store.Save(t2);
            var morning=store.Day(DateTime.Today).First(i=>i.TreatmentId==t.Id); store.Save(morning with {Status="taken",Taken=DateTime.Now});
            foreach(var entry in new[]{("Aujourd’hui","aujourdhui"),("Boîte à pharmacie","pharmacie"),("Calendrier","calendrier"),("Paramètres","parametres")}) {page=entry.Item1; Render(); Capture(this,"preview-"+entry.Item2);}
            var longName=t with {Id="preview-long-name",Name="Médicament avec un nom particulièrement long à afficher",Note="Une consigne assez longue pour vérifier sa lisibilité dans une fenêtre réduite.",Active=false}; store.Save(longName);
            Width=MinWidth; Height=MinHeight;
            foreach(var entry in new[]{("Aujourd’hui","aujourdhui"),("Boîte à pharmacie","pharmacie"),("Calendrier","calendrier"),("Paramètres","parametres")}) {page=entry.Item1; Render(); Capture(this,"preview-petite-"+entry.Item2);}
            page="Calendrier"; Render(); if(Find<Grid>(this,"history-layout").ColumnDefinitions[2].Width.Value!=0) throw new Exception("Le calendrier doit se placer au-dessus des détails dans une petite fenêtre.");
            store.Setting("large","true",true); ApplyFont(); page="Aujourd’hui"; Render(); Capture(this,"preview-petite-textes-agrandis");
            pageScroll.ScrollToVerticalOffset(100); UpdateLayout(); double savedOffset=pageScroll.VerticalOffset; Render(); UpdateLayout();
            if(Math.Abs(pageScroll.VerticalOffset-savedOffset)>1) throw new Exception("Le rafraîchissement doit conserver la position de lecture.");
            WindowState=WindowState.Maximized; await Task.Delay(250); page="Calendrier"; Render(); Capture(this,"preview-grand-calendrier");
            if(Find<Grid>(this,"history-layout").ColumnDefinitions[2].Width.Value==0) throw new Exception("Le calendrier doit se placer à côté des détails dans une grande fenêtre.");
            page="Boîte à pharmacie"; Render(); Capture(this,"preview-grand-pharmacie");
            WindowState=WindowState.Normal; Width=1080; Height=760; store.Setting("large","false",true); ApplyFont(); store.DeleteTreatment(longName.Id);
            var second=store.Day(DateTime.Today).Single(i=>i.TreatmentId==t2.Id && i.Due.Hour==12);
            foreach(var groupedPage in new[]{"Aujourd’hui","Calendrier"}) {
                page=groupedPage; Render();
                var noon=Find<Border>(this,"intake-group-1200");
                var labels=Walk(noon).OfType<TextBlock>().Select(b=>b.Text).ToList();
                if(labels.Count(s=>s=="12:00")!=1 || !labels.Contains(t.Name) || !labels.Contains(t2.Name) || !labels.Contains("3 gouttes") || !labels.Contains("2 médicaments · 1 prise validée sur 2")) throw new Exception("Les prises de même heure doivent partager un seul bandeau, avec leur quantité et état individuels.");
                ClickIn(this,"validate-"+second.Key);
                if(store.Day(DateTime.Today).Where(i=>i.Due.Hour==12).Any(i=>i.Status!="taken") || store.Day(DateTime.Today).Where(i=>i.Due.Hour==20).Any(i=>i.Status!="pending")) throw new Exception("La validation individuelle ne doit modifier que le médicament choisi à cet horaire.");
                if(!Walk(Find<Border>(this,"intake-group-1200")).OfType<TextBlock>().Any(b=>b.Text=="2 médicaments · 2 prises validées sur 2")) throw new Exception("Le compteur du groupe doit se mettre à jour après validation.");
                store.Save(second);
            }
            foreach(var intake in store.Day(DateTime.Today).Where(i=>i.TreatmentId==t2.Id)) store.Save(intake with {Status="taken",Taken=DateTime.Now});
            page="Boîte à pharmacie"; Render(); UpdateLayout(); if(!Walk(this).OfType<Button>().Any(b=>b.Content as string=="Supprimer")) throw new Exception("Le bouton Supprimer doit être présent.");
            var dialog=new TreatmentDialog(t) {Owner=this}; dialog.Show(); Capture(dialog,"preview-formulaire"); dialog.Close();
            var wizard=new TreatmentDialog(null) {Owner=this}; wizard.Show(); ClickIn(wizard,"wizard-next");
            if(string.IsNullOrEmpty(Find<TextBlock>(wizard,"wizard-error").Text)) throw new Exception("Le nom doit être obligatoire.");
            Find<TextBox>(wizard,"medicine-name").Text="Mon médicament"; Find<ComboBox>(wizard,"intake-count").SelectedItem=2; Capture(wizard,"preview-assistant-debut"); ClickIn(wizard,"wizard-next");
            ClickIn(wizard,"wizard-next"); if(string.IsNullOrEmpty(Find<TextBlock>(wizard,"wizard-error").Text)) throw new Exception("La quantité de la première prise doit être obligatoire.");
            var hour=Find<TimeWheel>(wizard,"hours-1"); hour.Value=23; hour.Value++; if(hour.Value!=0) throw new Exception("Le rouleau doit passer de 23 à 00."); hour.Value=12;
            Find<TextBox>(wizard,"dose-1").Text="1 cachet"; Capture(wizard,"preview-assistant-prise1"); ClickIn(wizard,"wizard-next");
            Find<TimeWheel>(wizard,"hours-2").Value=12; Find<TextBox>(wizard,"dose-2").Text="2 cachets"; ClickIn(wizard,"wizard-next");
            if(string.IsNullOrEmpty(Find<TextBlock>(wizard,"wizard-error").Text)) throw new Exception("Les horaires en double doivent être refusés.");
            Find<TimeWheel>(wizard,"hours-2").Value=20; Capture(wizard,"preview-assistant-prise2"); ClickIn(wizard,"wizard-next"); if(!Find<DatePicker>(wizard,"treatment-end").IsVisible) throw new Exception("Un traitement fixe doit conserver sa date de fin facultative."); Capture(wizard,"preview-assistant-jours"); ClickIn(wizard,"wizard-next");
            ClickIn(wizard,"wizard-back"); ClickIn(wizard,"wizard-back"); if(Find<TextBox>(wizard,"dose-2").Text!="2 cachets") throw new Exception("Le retour arrière doit conserver la quantité.");
            ClickIn(wizard,"wizard-next"); ClickIn(wizard,"wizard-next"); Capture(wizard,"preview-assistant-resume"); ClickIn(wizard,"wizard-next");
            if(wizard.Result?.Prises is not {Count:2} configured || configured[0]!=new DailyDose("12:00","1 cachet") || configured[1]!=new DailyDose("20:00","2 cachets")) throw new Exception("Assistant : programme enregistré incorrect.");
            var legacy=new TreatmentDialog(t with {Prises=null,Dose="3 gouttes",Times="08:00;20:00"}) {Owner=this}; legacy.Show(); ClickIn(legacy,"wizard-next"); if(Find<TextBox>(legacy,"dose-1").Text!="3 gouttes") throw new Exception("Première quantité ancienne perdue."); ClickIn(legacy,"wizard-next"); if(Find<TextBox>(legacy,"dose-2").Text!="3 gouttes") throw new Exception("Deuxième quantité ancienne perdue."); legacy.Close();
            var increasing=new TreatmentDialog(t with {Start=DateTime.Today,Prises=new(){new("20:00","1 cachet")}}) {Owner=this}; increasing.Show();
            Find<RadioButton>(increasing,"changing-mode").IsChecked=true; for(int n=0;n<3;n++) ClickIn(increasing,"wizard-next");
            ClickIn(increasing,"period-2-add-dose");
            if((int)Find<ComboBox>(increasing,"period-2-count").SelectedItem!=2 || Find<TextBox>(increasing,"period-2-dose-1").Text!="1 cachet") throw new Exception("Ajouter une prise doit augmenter uniquement le nombre de prises de la deuxième période et conserver la première prise.");
            ClickIn(increasing,"wizard-next"); if(string.IsNullOrEmpty(Find<TextBlock>(increasing,"wizard-error").Text)) throw new Exception("La quantité de la nouvelle prise doit être renseignée.");
            Find<TextBox>(increasing,"period-2-dose-2").Text="2 cachets"; Find<TimeWheel>(increasing,"period-2-hours-2").Value=8;
            Find<ComboBox>(increasing,"period-2-count").SelectedItem=1; Find<ComboBox>(increasing,"period-2-count").SelectedItem=2;
            if(Find<TextBox>(increasing,"period-2-dose-2").Text!="2 cachets") throw new Exception("Changer le nombre de prises doit conserver les valeurs déjà saisies.");
            Find<ComboBox>(increasing,"period-2-count").BringIntoView(); Capture(increasing,"preview-ajouter-prise-periode");
            Find<CheckBox>(increasing,"period-2-unlimited").IsChecked=false; Find<TextBox>(increasing,"period-2-duration").Text="14";
            ClickIn(increasing,"add-period"); ClickIn(increasing,"period-3-add-dose"); Find<TextBox>(increasing,"period-3-dose-3").Text="3 cachets";
            ClickIn(increasing,"wizard-next"); ClickIn(increasing,"wizard-next");
            var growing=increasing.Result??throw new Exception("Le programme avec des prises supplémentaires doit être enregistré.");
            if(Schedule.ForDay(growing,DateTime.Today.AddDays(13)).Count()!=1 || Schedule.ForDay(growing,DateTime.Today.AddDays(14)).Count()!=2 || Schedule.ForDay(growing,DateTime.Today.AddDays(28)).Count()!=3) throw new Exception("Les prises doivent passer de une à deux puis trois aux dates prévues.");
            var growingEdit=new TreatmentDialog(growing) {Owner=this}; growingEdit.Show(); for(int n=0;n<3;n++) ClickIn(growingEdit,"wizard-next");
            if((int)Find<ComboBox>(growingEdit,"period-2-count").SelectedItem!=2 || (int)Find<ComboBox>(growingEdit,"period-3-count").SelectedItem!=3 || !Walk(growingEdit).OfType<PeriodEditor>().Last().GetPeriod().Prises.SequenceEqual(growing.Periods![2].Prises)) throw new Exception("Les prises ajoutées doivent être conservées à la réouverture.");
            var retained=growing.Periods![2].Prises.Where((_,index)=>index!=1).ToList();
            ClickIn(growingEdit,"period-3-remove-dose-2");
            if((int)Find<ComboBox>(growingEdit,"period-3-count").SelectedItem!=3 || !Find<Button>(growingEdit,"period-3-confirm-remove-dose-2").IsVisible) throw new Exception("La poubelle doit demander confirmation avant toute suppression.");
            Find<Button>(growingEdit,"period-3-confirm-remove-dose-2").BringIntoView(); growingEdit.UpdateLayout(); Capture(growingEdit,"preview-confirmation-suppression-prise");
            ClickIn(growingEdit,"period-3-cancel-remove-dose-2");
            if((int)Find<ComboBox>(growingEdit,"period-3-count").SelectedItem!=3 || Find<Button>(growingEdit,"period-3-confirm-remove-dose-2").IsVisible) throw new Exception("Annuler doit conserver toutes les prises et fermer la confirmation.");
            ClickIn(growingEdit,"period-3-remove-dose-2"); ClickIn(growingEdit,"period-3-confirm-remove-dose-2");
            if((int)Find<ComboBox>(growingEdit,"period-3-count").SelectedItem!=2 || !Walk(growingEdit).OfType<PeriodEditor>().Last().GetPeriod().Prises.SequenceEqual(retained)) throw new Exception("La suppression confirmée doit retirer uniquement la prise choisie et conserver les autres horaires et quantités.");
            ClickIn(growingEdit,"period-3-add-dose"); if(Find<TextBox>(growingEdit,"period-3-dose-3").Text!="") throw new Exception("Une prise supprimée ne doit pas réapparaître lors d’un nouvel ajout.");
            ClickIn(growingEdit,"period-3-remove-dose-3"); ClickIn(growingEdit,"period-3-confirm-remove-dose-3");
            Find<ComboBox>(growingEdit,"period-3-count").SelectedItem=1; if(Find<Button>(growingEdit,"period-3-remove-dose-1").IsEnabled) throw new Exception("Une période doit conserver au moins une prise."); Find<ComboBox>(growingEdit,"period-3-count").SelectedItem=2;
            ClickIn(growingEdit,"wizard-next"); ClickIn(growingEdit,"wizard-next");
            var afterDeletion=growingEdit.Result??throw new Exception("Le programme après suppression doit être enregistré.");
            if(!afterDeletion.Periods![2].Prises.SequenceEqual(retained) || !afterDeletion.Periods[1].Prises.SequenceEqual(growing.Periods[1].Prises)) throw new Exception("La suppression doit être enregistrée sans modifier les autres périodes.");
            var phased=new TreatmentDialog(t with {Start=DateTime.Today,Prises=new(){new("08:00","1 cachet"),new("20:00","2 cachets")}}) {Owner=this}; phased.Show(); Find<RadioButton>(phased,"changing-mode").IsChecked=true; Capture(phased,"preview-assistant-choix");
            for(int n=0;n<3;n++) ClickIn(phased,"wizard-next"); if(Find<DatePicker>(phased,"treatment-end").IsVisible) throw new Exception("Un traitement par périodes ne doit pas afficher de fin globale."); Capture(phased,"preview-assistant-jours-periodes"); ClickIn(phased,"wizard-next");
            if(Find<CheckBox>(phased,"period-1-unlimited").IsVisible || Find<DatePicker>(phased,"period-1-end").IsVisible) throw new Exception("Une période intermédiaire ne doit pas proposer de fin du traitement.");
            var finalUnlimited=Find<CheckBox>(phased,"period-2-unlimited"); if(!finalUnlimited.IsVisible) throw new Exception("La dernière période doit proposer la fin du traitement."); finalUnlimited.IsChecked=false;
            var finalEnd=Find<DatePicker>(phased,"period-2-end"); if(!finalEnd.IsVisible) throw new Exception("La dernière période doit afficher sa date de fin."); finalEnd.SelectedDate=DateTime.Today.AddDays(30); if(Find<TextBox>(phased,"period-2-duration").Text!="17") throw new Exception("La date de fin doit recalculer la durée.");
            finalEnd.SelectedDate=DateTime.Today.AddDays(10); ClickIn(phased,"wizard-next"); if(string.IsNullOrEmpty(Find<TextBlock>(phased,"wizard-error").Text)) throw new Exception("La fin doit être postérieure au début de la dernière période."); finalUnlimited.IsChecked=true; Find<TextBox>(phased,"period-2-duration").Text="14";
            if(Find<DatePicker>(phased,"period-2-start").SelectedDate!=DateTime.Today.AddDays(14)) throw new Exception("La deuxième période doit commencer au quinzième jour.");
            Find<TextBox>(phased,"period-1-duration").Text="10"; if(Find<DatePicker>(phased,"period-2-start").SelectedDate!=DateTime.Today.AddDays(10)) throw new Exception("La durée doit mettre à jour la date suivante.");
            Find<DatePicker>(phased,"period-2-start").SelectedDate=DateTime.Today.AddDays(14); if(Find<TextBox>(phased,"period-1-duration").Text!="14") throw new Exception("Une date de changement doit recalculer la durée précédente.");
            Find<TextBox>(phased,"period-2-dose-1").Text="2 cachets"; Capture(phased,"preview-assistant-periodes"); ClickIn(phased,"add-period");
            if(Find<DatePicker>(phased,"period-3-start").SelectedDate!=DateTime.Today.AddDays(28)) throw new Exception("Ajout de plusieurs changements incorrect.");
            if(Find<DatePicker>(phased,"period-2-end").IsVisible || Find<CheckBox>(phased,"period-2-unlimited").IsVisible) throw new Exception("Ajouter une période doit déplacer les options de fin vers la dernière.");
            ClickIn(phased,"remove-period-3"); if(!Find<CheckBox>(phased,"period-2-unlimited").IsVisible) throw new Exception("Supprimer la dernière période doit rétablir les options sur la précédente."); ClickIn(phased,"add-period"); Find<TextBox>(phased,"period-3-dose-1").Text="3 cachets";
            Find<TextBox>(phased,"period-1-duration").Text="0"; ClickIn(phased,"wizard-next"); if(string.IsNullOrEmpty(Find<TextBlock>(phased,"wizard-error").Text)) throw new Exception("Une durée nulle doit être refusée.");
            Find<TextBox>(phased,"period-1-duration").Text="14"; ClickIn(phased,"wizard-next"); Capture(phased,"preview-assistant-periodes-resume"); ClickIn(phased,"wizard-back");
            if(Find<TextBox>(phased,"period-2-dose-1").Text!="2 cachets") throw new Exception("Les périodes doivent rester intactes après un retour arrière.");
            ClickIn(phased,"wizard-next"); ClickIn(phased,"wizard-next"); var savedPeriods=phased.Result ?? throw new Exception("Périodes non enregistrées.");
            if(savedPeriods.Periods?.Count!=3 || Schedule.ForDay(savedPeriods,DateTime.Today.AddDays(14)).First().Dose!="2 cachets" || Schedule.ForDay(savedPeriods,DateTime.Today.AddDays(28)).First().Dose!="3 cachets") throw new Exception("Quantités des périodes enregistrées incorrectes.");
            var editPeriods=new TreatmentDialog(savedPeriods) {Owner=this}; editPeriods.Show(); for(int n=0;n<4;n++) ClickIn(editPeriods,"wizard-next"); if(Find<TextBox>(editPeriods,"period-3-dose-1").Text!="3 cachets") throw new Exception("Édition des périodes existantes incorrecte.");
            Find<CheckBox>(editPeriods,"period-3-unlimited").IsChecked=false; Find<DatePicker>(editPeriods,"period-3-end").SelectedDate=DateTime.Today.AddDays(41); ClickIn(editPeriods,"wizard-next"); ClickIn(editPeriods,"wizard-next"); if(editPeriods.Result?.End!=DateTime.Today.AddDays(41) || Schedule.ForDay(editPeriods.Result,DateTime.Today.AddDays(42)).Any()) throw new Exception("La fin de la dernière période doit arrêter les prochaines prises.");
            var scrolling=new TreatmentDialog(savedPeriods) {Owner=this}; scrolling.Show(); for(int n=0;n<4;n++) ClickIn(scrolling,"wizard-next"); scrolling.Activate();
            var scroll=Find<ScrollViewer>(scrolling,"wizard-scroll"); var duration=Find<TextBox>(scrolling,"period-1-duration"); var count2=Find<ComboBox>(scrolling,"period-2-count");
            bool CountVisible() {
                scrolling.UpdateLayout(); var point=count2.TransformToAncestor(scroll).Transform(new Point());
                var title=Walk(scrolling).OfType<TextBlock>().Single(t=>t.Text=="Période 2"); var titlePoint=title.TransformToAncestor(scroll).Transform(new Point());
                return point.Y>=0 && point.Y+count2.ActualHeight<=scroll.ViewportHeight && titlePoint.Y>=0 && titlePoint.Y<80 && scroll.VerticalOffset<scroll.ScrollableHeight-1;
            }
            duration.Focus(); scroll.ScrollToTop(); scrolling.UpdateLayout(); duration.Text="1"; await Task.Delay(200); if(scroll.VerticalOffset>1) throw new Exception("Le défilement ne doit pas interrompre la saisie du premier chiffre."); duration.Text="14"; await Task.Delay(2400);
            if(scroll.VerticalOffset<=0 || !CountVisible() || !duration.IsKeyboardFocusWithin) throw new Exception("Après la durée, le nombre de prises doit être visible sans changer le focus."); Capture(scrolling,"preview-defilement-duree");
            scroll.ScrollToTop(); scrolling.UpdateLayout(); duration.Text="0"; await Task.Delay(2400); if(scroll.VerticalOffset>1) throw new Exception("Une durée invalide ne doit pas déclencher un défilement.");
            duration.Text="14"; duration.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            await Task.Delay(250); double earlyOffset=scroll.VerticalOffset;
            await Task.Delay(400); double middleOffset=scroll.VerticalOffset;
            await Task.Delay(750);
            if(earlyOffset<=0 || middleOffset<=earlyOffset || scroll.VerticalOffset<=middleOffset || !CountVisible()) throw new Exception("Le défilement doit progresser doucement pendant au moins une seconde et révéler les prises.");
            var date2=Find<DatePicker>(scrolling,"period-2-start"); scroll.ScrollToTop(); scrolling.UpdateLayout(); date2.IsDropDownOpen=true; date2.SelectedDate=DateTime.Today.AddDays(15); date2.IsDropDownOpen=false; await Task.Delay(1400);
            if(!CountVisible()) throw new Exception("Choisir une date doit révéler le nombre de prises de cette période."); Capture(scrolling,"preview-defilement-date");
            var caption=Walk(scrolling).OfType<TextBlock>().First(t=>t.Text.StartsWith("Quand la posologie"));
            void ClickBlank() => caption.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left) {RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent});
            duration.Focus(); duration.Text="14"; await Task.Delay(2400); scroll.ScrollToTop(); scrolling.UpdateLayout(); duration.Text="14"; ClickBlank(); await Task.Delay(1400);
            if(!CountVisible() || !duration.IsKeyboardFocusWithin) throw new Exception("Un clic sur un texte hors du champ doit défiler, même si la valeur et le focus ne changent pas.");
            scroll.ScrollToTop(); scrolling.UpdateLayout(); duration.Text="21"; ClickBlank(); await Task.Delay(1400); if(!CountVisible()) throw new Exception("Le clic hors du champ doit valider immédiatement la durée modifiée."); Capture(scrolling,"preview-defilement-clic-hors-champ");
            scroll.ScrollToTop(); scrolling.UpdateLayout(); duration.Text="0"; ClickBlank(); await Task.Delay(1400); if(scroll.VerticalOffset>1) throw new Exception("Un clic hors d’une durée invalide ne doit pas défiler.");
            duration.Text="14"; ClickBlank(); await Task.Delay(300);
            scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,120) {RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent});
            scroll.ScrollToTop(); scrolling.UpdateLayout(); await Task.Delay(1400);
            if(scroll.VerticalOffset>1) throw new Exception("La molette doit interrompre le défilement automatique.");
            var duration2=Find<TextBox>(scrolling,"period-2-duration"); duration2.Focus(); duration2.Text="21";
            duration2.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)); await Task.Delay(1500);
            var title3=Walk(scrolling).OfType<TextBlock>().Single(t=>t.Text=="Période 3"); var title3Point=title3.TransformToAncestor(scroll).Transform(new Point());
            if(title3Point.Y<0 || title3Point.Y>80) throw new Exception("La durée de la deuxième période doit révéler le début de la troisième période.");
            Capture(scrolling,"preview-defilement-periode-suivante");
            scrolling.WindowState=WindowState.Maximized; await Task.Delay(250); scrolling.UpdateLayout();
            if(Find<TextBox>(scrolling,"period-2-dose-1").ActualWidth>221 || Find<Button>(scrolling,"add-period").ActualWidth>400) throw new Exception("Les champs de quantité et les boutons doivent rester compacts dans une fenêtre maximisée.");
            Find<ComboBox>(scrolling,"period-2-count").BringIntoView(); scrolling.UpdateLayout(); Capture(scrolling,"preview-periodes-fenetre-maximisee"); scrolling.Close();
            ShowReminder(true); if(reminder!=null) {Capture(reminder,"preview-rappel"); reminder.Close();}
            var testIntake=new Intake("ui-test",t.Id,"Test de prise","1 comprimé","",DateTime.Now.AddMinutes(-1)); store.Save(testIntake);
            ShowReminder(false); if(reminder==null || !reminder.Topmost) throw new Exception("Le rappel doit apparaître au premier plan."); Capture(reminder,"preview-rappel-reel");
            void Click(string text) {reminder!.UpdateLayout(); var button=Walk(reminder).OfType<Button>().First(b=>b.Content as string==text); button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
            Click("Valider la prise"); if(store.Intakes().Single(i=>i.Key==testIntake.Key).Status!="taken" || reminder!=null) throw new Exception("Validation du rappel incorrecte.");
            foreach(int minutes in new[]{15,30,60}) foreach(bool all in new[]{false,true}) {
                store.Save(testIntake); ShowReminder(false); var before=DateTime.Now;
                Click((all?"Tout reporter de ":"Reporter de ")+(minutes==60?"1 h":$"{minutes} min"));
                var saved=store.Intakes().Single(i=>i.Key==testIntake.Key);
                if(saved.Snooze<before.AddMinutes(minutes) || saved.Snooze>DateTime.Now.AddMinutes(minutes) || saved.Snooze==null || saved.Status!="pending" || Due().Any(i=>i.Key==testIntake.Key) || reminder!=null) throw new Exception($"Report de {minutes} minutes incorrect (tout : {all}).");
            }
            var changeTreatment=t with {Id="ui-dosage-change",Name="Test changement de dosage",Days=127,Start=DateTime.Today.AddDays(-1),End=null,Periods=new(){new(DateTime.Today.AddDays(-1),DateTime.Today.AddDays(-1),new(){new("00:00","1 comprimé")}),new(DateTime.Today,null,new(){new("00:00","2 comprimés")})}};
            store.Save(changeTreatment); page="Aujourd’hui"; Render();
            var changeNotice=Walk(this).OfType<Border>().First(b=>System.Windows.Automation.AutomationProperties.GetAutomationId(b)=="posology-change-"+changeTreatment.Id);
            var noticeLabels=Walk(changeNotice).OfType<TextBlock>().Select(b=>b.Text).ToList();
            if(!noticeLabels.Any(s=>s.Contains("Changement de dosage")) || !noticeLabels.Any(s=>s.Contains("Avant : 1 comprimé")) || !noticeLabels.Any(s=>s.Contains("À partir d’aujourd’hui : 2 comprimés"))) throw new Exception("Le changement de dosage doit afficher les anciennes et nouvelles quantités dans Aujourd’hui.");
            pageScroll.ScrollToTop(); Capture(this,"preview-changement-dosage"); ShowReminder(false);
            if(reminder==null || !Walk(reminder).OfType<TextBlock>().Any(b=>b.Text.Contains("Changement de dosage"))) throw new Exception("Le rappel doit signaler le changement de dosage.");
            Capture(reminder,"preview-rappel-changement-dosage"); reminder.Close();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-verification.txt"),"OK : Aujourd’hui et Calendrier regroupent les médicaments de même horaire, quantités et états individuels, validation isolée et compteur actualisé ; défilement limité à la période suivante ; assistant et rappels vérifiés.");
            quitting=true; tray.Dispose(); timer.Stop(); store.Dispose(); System.Windows.Application.Current.Shutdown();
        }));
    }
}
