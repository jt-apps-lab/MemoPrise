using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MemoPrise;

internal static class TrayAnimation
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")]
    static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongW")]
    static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static Window? Play(Window source)
    {
        if(!SystemParameters.ClientAreaAnimation || !source.IsVisible || source.WindowState==WindowState.Minimized) return null;
        var taskbar=FindWindow("Shell_TrayWnd",null);
        var notificationArea=FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
        if(taskbar==IntPtr.Zero || !GetWindowRect(notificationArea!=IntPtr.Zero?notificationArea:taskbar,out var tray)) return null;
        double targetX=(tray.Left+tray.Right)/2.0, targetY=(tray.Top+tray.Bottom)/2.0;
        if(notificationArea==IntPtr.Zero) {
            if(tray.Right-tray.Left>tray.Bottom-tray.Top) targetX=tray.Right-40;
            else targetY=tray.Bottom-40;
        }

        Window? overlay=null;
        try {
            source.UpdateLayout();
            var origin=source.PointToScreen(new Point());
            var sourceDpi=VisualTreeHelper.GetDpi(source);
            double pixelWidth=source.ActualWidth*sourceDpi.DpiScaleX, pixelHeight=source.ActualHeight*sourceDpi.DpiScaleY;
            double captureScale=Math.Min(sourceDpi.DpiScaleX,1600/source.ActualWidth);
            var snapshot=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(source.ActualWidth*captureScale)),Math.Max(1,(int)Math.Ceiling(source.ActualHeight*captureScale)),96*captureScale,96*captureScale,PixelFormats.Pbgra32);
            snapshot.Render(source); snapshot.Freeze();
            int left=(int)Math.Floor(Math.Min(origin.X,targetX)-20), top=(int)Math.Floor(Math.Min(origin.Y,targetY)-20);
            int right=(int)Math.Ceiling(Math.Max(origin.X+pixelWidth,targetX)+20), bottom=(int)Math.Ceiling(Math.Max(origin.Y+pixelHeight,targetY)+20);
            var canvas=new Canvas {IsHitTestVisible=false};
            overlay=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false,Left=source.Left,Top=source.Top,Width=source.ActualWidth,Height=source.ActualHeight,Content=canvas};
            overlay.Show();
            var handle=new WindowInteropHelper(overlay).Handle;
            // The animation must not intercept clicks or activate a second window.
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x20|0x80|0x08000000);
            if(!SetWindowPos(handle,new IntPtr(-1),left,top,right-left,bottom-top,0x10)) {overlay.Close(); return null;}
            overlay.UpdateLayout();
            var start=overlay.PointFromScreen(origin);
            var finish=overlay.PointFromScreen(new Point(targetX,targetY));
            var overlayDpi=VisualTreeHelper.GetDpi(overlay);
            var scale=new ScaleTransform(1,1);
            var move=new TranslateTransform();
            var transforms=new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(move);
            var image=new Image {Source=snapshot,Width=pixelWidth/overlayDpi.DpiScaleX,Height=pixelHeight/overlayDpi.DpiScaleY,Stretch=Stretch.Fill,RenderTransform=transforms};
            Canvas.SetLeft(image,start.X); Canvas.SetTop(image,start.Y); canvas.Children.Add(image);
            var duration=TimeSpan.FromMilliseconds(450);
            double finalScale=28/Math.Max(pixelWidth,pixelHeight);
            DoubleAnimation Animate(double to)=>new(to,duration) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseInOut}};
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,Animate(finalScale));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,Animate(finalScale));
            move.BeginAnimation(TranslateTransform.XProperty,Animate(finish.X-start.X-image.Width*finalScale/2));
            move.BeginAnimation(TranslateTransform.YProperty,Animate(finish.Y-start.Y-image.Height*finalScale/2));
            var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(120)) {BeginTime=TimeSpan.FromMilliseconds(330)};
            var animationWindow=overlay;
            fade.Completed+=(_,_)=>animationWindow.Close();
            image.BeginAnimation(UIElement.OpacityProperty,fade);
            return overlay;
        } catch(InvalidOperationException) {overlay?.Close(); return null;}
          catch(ArgumentException) {overlay?.Close(); return null;}
    }
}
