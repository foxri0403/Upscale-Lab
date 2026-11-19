using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using UpscaleLab.Companion;
using Xunit;

public sealed class ParallaxRenderingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FramesContinueInAHiddenDesktopChildWithoutTouchingOsWallpaper(bool desktopChild, bool character)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{
            var directory=Path.Combine(Path.GetTempPath(),"upscale-render-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try {
                var file=Path.Combine(directory,"layer.png");using(var bitmap=new Bitmap(64,64)){using var graphics=Graphics.FromImage(bitmap);graphics.Clear(System.Drawing.Color.Blue);bitmap.Save(file);}
                var reads=0;var input=new Point(320,120);
                var monitor=new MonitorInfo("test","Test",320,240,1,0,0,0,true,true,60);
                var manifest=new WallpaperManifest(1,Guid.NewGuid(),"Live2D",64,64,
                    [new(file,"image/png",0,character ? "BackHair" : "Background"),new(file,"image/png",1,character ? "Face" : "Body",MovementX:10,MovementY:10)],
                    Rig:character ? new CharacterRig(Enabled:true) : null);
                var layers=Task.Run(()=>WallpaperLayers.Load(manifest,monitor,Path.Combine(directory,"neutral.png"))).GetAwaiter().GetResult();
                Assert.All(layers,layer=>Assert.True(layer.Image.IsFrozen));
                using var neutral=new Bitmap(Path.Combine(directory,"neutral.png"));
                Assert.Equal(System.Drawing.Color.Blue.ToArgb(),neutral.GetPixel(160,120).ToArgb());
                using var view=new ParallaxView(monitor,manifest,layers,()=>{reads++;return input;},playbackPaused:true);
                using var form=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-4000,-4000),Size=new Size(320,240)};
                using var host=new ElementHost {Dock=DockStyle.Fill,Child=view};form.Controls.Add(host);
                if(desktopChild){var worker=DesktopHost.Find(true);Assert.NotEqual(IntPtr.Zero,worker);DesktopHost.Parent(form.Handle,worker,new Rectangle(-4000,-4000,320,240));}
                form.Show();
                PumpFrames(250);
                Assert.True(view.IsLoaded,$"WPF not loaded; desktopChild={desktopChild}");
                Assert.Equal(0,reads);
                view.SetPlaybackPaused(false);
                PumpFrames(900);
                var canvas=(System.Windows.Controls.Canvas)view.Children[0];
                var plane=(System.Windows.Controls.Image)canvas.Children[1];
                var shift=((TransformGroup)plane.RenderTransform).Children.OfType<TranslateTransform>().Single();
                var rotation=((TransformGroup)plane.RenderTransform).Children.OfType<RotateTransform>().Last();
                Assert.True(reads>10,$"No sustained render frames: reads={reads}; desktopChild={desktopChild}");
                Assert.True(shift.X>5,$"Movement must follow the mouse to the right: x={shift.X}; frames={reads}; desktopChild={desktopChild}");
                view.SetPlaybackPaused(true);
                var pausedReads=reads;
                var pausedX=shift.X;
                var pausedAngle=rotation.Angle;
                input=new Point(0,120);
                PumpFrames(250);
                Assert.Equal(pausedReads,reads);
                Assert.Equal(pausedX,shift.X);
                Assert.Equal(pausedAngle,rotation.Angle);
                view.SetPlaybackPaused(false);
                PumpFrames(900);
                Assert.True(reads>pausedReads);
                Assert.True(shift.X < -5,$"Movement must resume toward the new pointer: x={shift.X}");
                if (character) Assert.True(pausedAngle > 0 && rotation.Angle < 0);
                form.Close();
            }catch(Exception error){failure=error;}finally{Directory.Delete(directory,true);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(15)));if(failure!=null)throw failure;
    }

    [Fact]
    public void BodyMotionIsVisibleWhenEnabledAndStillRespectsFixedMode()
    {
        var follow = new CharacterRig(Enabled:true, BodyMode:"follow");
        var fixedRig = follow with { BodyMode = "fixed" };
        var torso = RigMotion.AngleDegrees("torso", follow, 1);
        Assert.InRange(torso, 1.4, 2.2);
        Assert.Equal(0, RigMotion.AngleDegrees("torso", fixedRig, 1));
        Assert.True(RigMotion.BodyShift("torso", follow, 1, 0.5, 1200, 2000).X > 0);
        Assert.Equal((0.0, 0.0), RigMotion.BodyShift("torso", fixedRig, 1, 0.5, 1200, 2000));
    }

    [Fact]
    public void EyeGazeMovesRelativeToHeadAndRespectsFaceAndEyeSettings()
    {
        var rig = new CharacterRig(Enabled: true);
        var head = RigMotion.FaceShift("head", rig, 1, 1, 1000, 2000);
        var eye = RigMotion.FaceShift("eye", rig, 1, 1, 1000, 2000);
        Assert.Equal(6.3, eye.X - head.X, 6);
        Assert.Equal(9.8, eye.Y - head.Y, 6);
        Assert.Equal(head, RigMotion.FaceShift("eye", rig with { EyeMove = 0 }, 1, 1, 1000, 2000));
        Assert.Equal((0.0, 0.0), RigMotion.FaceShift("eye", rig with { FaceEnabled = false }, 1, 1, 1000, 2000));
        Assert.Equal((0.0, 0.0), RigMotion.FaceShift("fixed", rig, 1, 1, 1000, 2000));
        Assert.Equal(0, RigMotion.AngleDegrees("head", rig with { FaceEnabled = false }, 1));
        Assert.True(RigMotion.AngleDegrees("torso", rig with { FaceEnabled = false, BodyMode = "follow" }, 1) > 0);
    }

    private static void PumpFrames(int milliseconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(2);
        }
    }
}
