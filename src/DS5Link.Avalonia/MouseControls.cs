using System.Diagnostics;

namespace DS5Link;

internal sealed class MouseMotion
{
    TouchPreview? previous;
    double remainderX,remainderY;
    long last;

    internal void Reset() { previous=null;last=0;remainderX=remainderY=0; }

    internal (int X,int Y) Read(InputPreview sample, InputOptions options, long now)
    {
        if (!sample.Fresh || sample.AgeMs>70) { Reset();return default; }
        var elapsed=last==0?0:Stopwatch.GetElapsedTime(last,now).TotalSeconds;
        last=now;
        if(elapsed>0.06){Reset();last=now;elapsed=0;}
        double x=0,y=0;
        if(options.GyroMouse && sample.AimHeld)
        {
            static double Rate(int raw) => Math.Sign(raw)*Math.Max(0,Math.Abs(Math.Clamp(raw,-32768,32767))-32);
            x-=Rate(sample.GyroY)*elapsed*0.35;
            y-=Rate(sample.GyroX)*elapsed*0.35;
        }
        TouchPreview? contact=null;
        if(options.TouchMouse && sample.Touches is {Length:2} touches && touches[0].Active!=touches[1].Active)
            contact=touches[0].Active?touches[0]:touches[1];
        if(contact!=null && previous!=null && contact.Id==previous.Id)
        {
            x+=Math.Clamp(contact.X-previous.X,-220,220)*0.75;
            y+=Math.Clamp(contact.Y-previous.Y,-220,220)*0.75;
        }
        previous=contact;
        var scale=Math.Clamp(options.MouseSensitivity,25,300)/100.0;
        remainderX+=x*scale;remainderY+=y*scale;
        int dx=(int)remainderX,dy=(int)remainderY;
        remainderX-=dx;remainderY-=dy;
        return (dx,dy);
    }
}

internal sealed partial class MainWindow
{
    readonly DispatcherTimer mouseTimer=new(){Interval=TimeSpan.FromMilliseconds(10)};
    readonly MouseMotion mouseMotion=new();
    bool mousePolling;

    void SyncMouseTimer()
    {
        mouseMotion.Reset();
        if(!preview && (inputProfile.Options.GyroMouse || inputProfile.Options.TouchMouse))mouseTimer.Start();
        else mouseTimer.Stop();
    }

    bool MouseAllowed()=>current.State=="Active" && !profileSwitching && !commanding && !exiting &&
        !exitRequested && !IsActive && inputSettingsWindow?.IsActive!=true && ProfileAllowsKeyboard();

    async Task PollMouse()
    {
        if(mousePolling)return;
        if(!MouseAllowed()){mouseMotion.Reset();return;}
        mousePolling=true;
        var generation=stateGeneration;
        try
        {
            var requestStarted=Stopwatch.GetTimestamp();
            var sample=await BridgeClient.ReadInputPreviewAsync();
            if(generation!=stateGeneration || Stopwatch.GetElapsedTime(requestStarted).TotalMilliseconds>60 || !MouseAllowed())
            { mouseMotion.Reset();return; }
            var (x,y)=mouseMotion.Read(sample,inputProfile.Options,Stopwatch.GetTimestamp());
            KeyboardOutput.MoveMouse(x,y);
        }
        catch(Exception){mouseMotion.Reset();}
        finally{mousePolling=false;}
    }
}
