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
    [InlineData(false)]
    [InlineData(true)]
    public void FramesContinueInAHiddenDesktopChildWithoutTouchingOsWallpaper(bool desktopChild)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{
            var directory=Path.Combine(Path.GetTempPath(),"upscale-render-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try {
                var file=Path.Combine(directory,"layer.png");using(var bitmap=new Bitmap(64,64)){using var graphics=Graphics.FromImage(bitmap);graphics.Clear(System.Drawing.Color.Blue);bitmap.Save(file);}
                var reads=0;var input=new Point(320,120);
                var monitor=new MonitorInfo("test","Test",320,240,1,0,0,0,true,true,60);
                var manifest=new WallpaperManifest(1,Guid.NewGuid(),"Live2D",64,64,[new(file,"image/png",0,"Background"),new(file,"image/png",1,"Body",MovementX:10,MovementY:10)]);
                using var view=new ParallaxView(monitor,manifest,()=>{reads++;return input;});
                using var form=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-4000,-4000),Size=new Size(320,240)};
                using var host=new ElementHost {Dock=DockStyle.Fill,Child=view};form.Controls.Add(host);
                if(desktopChild){var worker=DesktopHost.Find(true);Assert.NotEqual(IntPtr.Zero,worker);DesktopHost.Parent(form.Handle,worker,new Rectangle(-4000,-4000,320,240));}
                form.Show();var elapsed=Stopwatch.StartNew();while(elapsed.ElapsedMilliseconds<900){Application.DoEvents();Thread.Sleep(2);}
                var canvas=(System.Windows.Controls.Canvas)view.Children[0];
                var plane=(System.Windows.Controls.Image)canvas.Children[1];
                var shift=(TranslateTransform)((TransformGroup)plane.RenderTransform).Children[2];
                Assert.True(view.IsLoaded,$"WPF not loaded; desktopChild={desktopChild}");
                Assert.True(reads>10,$"No sustained render frames: reads={reads}; desktopChild={desktopChild}");
                Assert.True(shift.X>5,$"Movement must follow the mouse to the right: x={shift.X}; frames={reads}; desktopChild={desktopChild}");
                form.Close();
            }catch(Exception error){failure=error;}finally{Directory.Delete(directory,true);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(15)));if(failure!=null)throw failure;
    }
}
