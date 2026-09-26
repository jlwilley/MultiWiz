using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;

namespace MultiWiz.Core.Tests;

public sealed class AudioPolicyTests
{
    private static readonly AudioSettings Ducking = new() { Enabled = true, FocusedVolumePercent = 90, UnfocusedVolumePercent = 15 };

    [Fact]
    public void Focused_client_gets_the_focused_volume_and_the_rest_the_unfocused_volume()
    {
        var targets = AudioPolicy.Compute([11, 22, 33], 22, Ducking);

        Assert.Equal(new[] { new VolumeTarget(11, 15), new VolumeTarget(22, 90), new VolumeTarget(33, 15) }, targets.ToArray());
    }

    [Fact]
    public void Everyone_gets_the_focused_volume_when_nothing_is_focused()
    {
        var targets = AudioPolicy.Compute([11, 22], null, Ducking);

        Assert.All(targets, target => Assert.Equal(90, target.VolumePercent));
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void Default_settings_mute_background_clients()
    {
        var targets = AudioPolicy.Compute([1, 2], 1, new AudioSettings());

        Assert.Equal(new[] { new VolumeTarget(1, 100), new VolumeTarget(2, 0) }, targets.ToArray());
    }

    [Fact]
    public void Disabled_policy_changes_nothing()
    {
        Assert.Empty(AudioPolicy.Compute([11, 22], 11, Ducking with { Enabled = false }));
    }

    [Fact]
    public void No_processes_means_no_targets()
    {
        Assert.Empty(AudioPolicy.Compute([], 11, Ducking));
    }

    [Fact]
    public void Volumes_are_clamped_and_bad_process_ids_ignored()
    {
        var settings = new AudioSettings { Enabled = true, FocusedVolumePercent = 250, UnfocusedVolumePercent = -20 };

        var targets = AudioPolicy.Compute([5, 0, -3, 6, 5], 5, settings);

        Assert.Equal(new[] { new VolumeTarget(5, 100), new VolumeTarget(6, 0) }, targets.ToArray());
    }
}
