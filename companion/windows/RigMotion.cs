using System;
namespace UpscaleLab.Companion;

/** PNG-based, role-aware desktop motion. Each part must exist as an independent PNG. */
internal static class RigMotion
{
    internal static string Part(WallpaperFile file, CharacterRig rig)
    {
        if (file.LayerId != null && rig.PartAssignments?.TryGetValue(file.LayerId, out var assigned) == true)
            return assigned;
        return file.Role switch
        {
            "Face" or "Eyebrow" or "Mouth" or "Nose" => "head",
            "Eye" => "eye", "BackHair" => "backHair", "FrontHair" or "Hair" => "frontHair",
            "Body" or "Clothes" => "torso", _ => "fixed"
        };
    }
    internal static double AngleDegrees(string part, CharacterRig rig, double tiltX)
    {
        if (!rig.Enabled || part == "fixed") return 0;
        double head = Math.Clamp(tiltX, -1, 1) * rig.HeadTilt / 100.0 * .115;
        double upper = rig.BodyMode == "follow" ? head * rig.UpperBody / 100.0 : 0;
        double lower = rig.BodyMode == "follow" ? head * rig.LowerBody / 100.0 : 0;
        double torso = upper * rig.Torso / 100.0 * .6;
        double leftArm = torso + upper * rig.LeftArm / 100.0 * .42;
        double rightArm = torso + upper * rig.RightArm / 100.0 * .42;
        double lowerBody = lower * .32;
        double rad = part switch
        {
            "head" or "eye" => head,
            "frontHair" => head * (1 - .55 * rig.HairSway / 100.0),
            "backHair" => head * (1 - .28 * rig.HairSway / 100.0),
            "torso" => torso,
            "leftArm" => leftArm, "rightArm" => rightArm,
            "leftHand" => leftArm + upper * rig.LeftHand / 100.0 * .24,
            "rightHand" => rightArm + upper * rig.RightHand / 100.0 * .24,
            "lowerBody" => lowerBody,
            "leftLeg" => lowerBody + lower * rig.LeftLeg / 100.0 * .18,
            "rightLeg" => lowerBody + lower * rig.RightLeg / 100.0 * .18,
            _ => 0
        };
        return rad * 180.0 / Math.PI;
    }
    internal static double PivotY(string part, CharacterRig rig) => part switch
    {
        "torso" or "leftArm" or "rightArm" or "leftHand" or "rightHand" => rig.TorsoPivotY,
        "lowerBody" or "leftLeg" or "rightLeg" => rig.LowerPivotY,
        _ => rig.PivotY
    };
}
