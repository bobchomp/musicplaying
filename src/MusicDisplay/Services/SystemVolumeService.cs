using NAudio.CoreAudioApi;

namespace MusicDisplay.Services;

/// <summary>
/// Controls the system's master output volume (the same slider as the Windows volume flyout),
/// via NAudio's wrapper around the Windows Core Audio API. This is whole-PC volume, not
/// per-application volume.
/// </summary>
public static class SystemVolumeService
{
    public static float GetVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    public static void SetVolume(float level)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
        }
        catch (Exception)
        {
            // No default playback device, or the audio subsystem is unavailable.
        }
    }

    public static bool GetMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetMute(bool mute)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = mute;
        }
        catch (Exception)
        {
            // No default playback device, or the audio subsystem is unavailable.
        }
    }
}
