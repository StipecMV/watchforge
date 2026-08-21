namespace WatchForge.Interfaces.Library.Tests;

/// <summary>
/// S2-1: overuje doménové konštanty a modely v Interfaces.Library
/// (čisté rozhrania nemajú logiku — testujú sa obchodné pravidlá: priority, stavy).
/// </summary>
public class DomainModelTests
{
    [Test]
    public async Task JobPriority_Ordering_MatchesSourceRanking()
    {
        // Given priority konštanty (FR-08: WhatsApp > Telegram > WebUI > System > Background)
        // When porovnáme
        // Then whatsapp je najvyššia a background najnižšia
        await Assert.That(JobPriority.WhatsApp).IsGreaterThan(JobPriority.Telegram);
        await Assert.That(JobPriority.Telegram).IsGreaterThan(JobPriority.WebUi);
        await Assert.That(JobPriority.WebUi).IsGreaterThan(JobPriority.System);
        await Assert.That(JobPriority.System).IsGreaterThan(JobPriority.Background);
    }

    [Test]
    public async Task JobPriority_Values_AreStable()
    {
        // Given stabilné priority (zmena by rozbila existujúce joby v DB)
        await Assert.That(JobPriority.WhatsApp).IsEqualTo(100);
        await Assert.That(JobPriority.Telegram).IsEqualTo(90);
        await Assert.That(JobPriority.WebUi).IsEqualTo(80);
        await Assert.That(JobPriority.System).IsEqualTo(50);
        await Assert.That(JobPriority.Background).IsEqualTo(10);
    }

    [Test]
    public async Task DetectionProfile_Defaults_AreSane()
    {
        // Given predvolený profil
        var profile = DetectionProfile.Default;

        // Then základné hodnoty sú rozumné
        await Assert.That(profile.Sensitivity).IsGreaterThan(0f);
        await Assert.That(profile.Sensitivity).IsLessThanOrEqualTo(1f);
        await Assert.That(profile.IgnoreZones).IsNotNull();
        await Assert.That(profile.FocusZones).IsNotNull();
        await Assert.That(profile.ProfileType).IsEqualTo("per_camera");
    }

    [Test]
    public async Task JobStatus_ValidTransitions_AreAllowed()
    {
        // Given job v stave queued
        var state = JobStatus.Queued;

        // When prejde do running
        var allowed = state.CanTransitionTo(JobStatus.Running);

        // Then je to povolené
        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task JobStatus_RunningToCompleted_IsAllowed()
    {
        // Given job v stave running
        var state = JobStatus.Running;

        // When prejde do completed / failed / interrupted
        // Then všetky sú povolené
        await Assert.That(state.CanTransitionTo(JobStatus.Completed)).IsTrue();
        await Assert.That(state.CanTransitionTo(JobStatus.Failed)).IsTrue();
        await Assert.That(state.CanTransitionTo(JobStatus.Interrupted)).IsTrue();
    }

    [Test]
    public async Task JobStatus_QueuedToCompleted_IsNotAllowed()
    {
        // Given job v stave queued
        var state = JobStatus.Queued;

        // When skočí rovno do completed
        // Then to NIE je povolené (musí ísť cez running)
        await Assert.That(state.CanTransitionTo(JobStatus.Completed)).IsFalse();
    }
}
