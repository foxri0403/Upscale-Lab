using UpscaleLab.Application.Images;
using UpscaleLab.Application.Common;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class CharacterRigEditTests
{
    [Fact]
    public void RigSettings_SerializeAndRestoreThroughExistingSavedEditJson()
    {
        var rig = new HeadRigSettings(true, 60, 35, 55, 50, 16, true);
        var request = new SaveImageEditRequest(70, "original", "original", 1, true, Rig: rig);
        var saved = ImageEditState.Create(request);
        var restored = ImageEditState.FromJson(saved.ToJson());
        Assert.Equal(rig, restored?.Rig);
    }

    [Fact]
    public void RigSettings_RejectOutOfRangeValues()
    {
        var request = new SaveImageEditRequest(70, "original", "original", 1, true,
            Rig: new HeadRigSettings(true, 101, 35, 55, 50, 16, true));
        Assert.Throws<ValidationException>(() => ImageEditState.Create(request));
    }
}
