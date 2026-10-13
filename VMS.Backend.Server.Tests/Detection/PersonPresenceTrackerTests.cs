using VMS.MetadataIndexer.Detection;

namespace VMS.Backend.Server.Tests.Detection;

public class PersonPresenceTrackerTests
{
    [Fact]
    public void First_frame_reports_every_centroid_as_a_new_sighting()
    {
        var tracker = new PersonPresenceTracker();

        var newSightings = tracker.ProcessFrame([(0.2, 0.2), (0.8, 0.8)]);

        Assert.Equal([0, 1], newSightings);
    }

    [Fact]
    public void A_person_standing_still_is_not_re_counted_on_the_next_frame()
    {
        var tracker = new PersonPresenceTracker();
        tracker.ProcessFrame([(0.5, 0.5)]);

        var newSightings = tracker.ProcessFrame([(0.5, 0.5)]);

        Assert.Empty(newSightings);
    }

    [Fact]
    public void A_brief_absence_within_the_grace_window_is_not_re_counted_on_return()
    {
        // MaxMissedFrames is tuned to 6 specifically so a person stepping out of frame for a few
        // snapshots (then reappearing in roughly the same spot) isn't double-counted — this is
        // the exact scenario the README flags as the report's main known accuracy gap.
        var tracker = new PersonPresenceTracker();
        tracker.ProcessFrame([(0.5, 0.5)]);

        for (var i = 0; i < 5; i++)
        {
            Assert.Empty(tracker.ProcessFrame([]));
        }

        var newSightings = tracker.ProcessFrame([(0.5, 0.5)]);

        Assert.Empty(newSightings);
    }

    [Fact]
    public void An_absence_longer_than_the_grace_window_counts_as_a_new_sighting_on_return()
    {
        var tracker = new PersonPresenceTracker();
        tracker.ProcessFrame([(0.5, 0.5)]);

        for (var i = 0; i < 7; i++)
        {
            tracker.ProcessFrame([]);
        }

        var newSightings = tracker.ProcessFrame([(0.5, 0.5)]);

        Assert.Equal([0], newSightings);
    }

    [Fact]
    public void A_centroid_far_from_any_existing_track_is_a_new_sighting()
    {
        var tracker = new PersonPresenceTracker();
        tracker.ProcessFrame([(0.1, 0.1)]);

        var newSightings = tracker.ProcessFrame([(0.1, 0.1), (0.9, 0.9)]);

        Assert.Equal([1], newSightings);
    }
}
