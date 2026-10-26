namespace UpscaleLab.Domain.Enums;

public enum WallpaperMode { Static, Live2D }
public enum WallpaperTarget { Desktop, Home, Lock }
public enum WallpaperObservation { Unknown, Managed, External, Unavailable }
public enum DeviceCommandStatus { Pending, Downloading, NeedsUserAction, Applied, Failed, Superseded, Expired }
public enum ProcessingOperation { Layers, Upscale, UpscaleAndLayers }
