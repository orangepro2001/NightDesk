package dev.nightdesk.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NxConnectionTest {
    private var now = 1_000L
    private val model = NxConnectionModel { now }

    @Test
    fun reachesStreamingAfterConnecting() {
        val attempt = model.begin(pairingCodeProvided = true)
        assertEquals(NxConnectionPhase.Connecting, model.snapshot().phase)
        assertTrue(model.state(attempt, "connecting"))
        assertTrue(model.state(attempt, "streaming"))
        val snapshot = model.snapshot()
        assertEquals(NxConnectionPhase.Streaming, snapshot.phase)
        assertEquals(null, snapshot.failure)
        assertEquals(0L, snapshot.deadlineMillis)
    }

    @Test
    fun timeoutRequiresDeadlineThenRetryStartsFreshAttempt() {
        val first = model.begin(pairingCodeProvided = false, timeoutMillis = 500)
        now += 499
        assertFalse(model.timeout(first))
        now += 1
        assertTrue(model.timeout(first))
        assertEquals("timeout", model.snapshot().failure?.code)
        assertTrue(model.snapshot().canRetry)

        val second = model.begin(pairingCodeProvided = false, timeoutMillis = 500)
        assertTrue(second > first)
        assertEquals(NxConnectionPhase.Connecting, model.snapshot().phase)
        assertFalse(model.timeout(first))
    }

    @Test
    fun staleCallbacksCannotOverwriteANewerAttempt() {
        val first = model.begin(pairingCodeProvided = true)
        val second = model.begin(pairingCodeProvided = false)
        assertFalse(model.state(first, "streaming"))
        assertFalse(model.error(first, "host-identity"))
        assertFalse(model.closed(first))
        assertTrue(model.state(second, "streaming"))
        assertEquals(second, model.snapshot().attempt)
        assertEquals(NxConnectionPhase.Streaming, model.snapshot().phase)
    }

    @Test
    fun badPairingAndUntrustedHostRequirePairingAgain() {
        listOf("bad-code", "untrusted").forEach { code ->
            val attempt = model.begin(pairingCodeProvided = true)
            assertTrue(model.error(attempt, code))
            val snapshot = model.snapshot()
            assertEquals(NxRecovery.PairAgain, snapshot.failure?.recovery)
            assertTrue(snapshot.canPairAgain)
            assertFalse(snapshot.canRetry)
        }
    }

    @Test
    fun fingerprintMismatchIsTerminalAndNeverOffersRetryOrCodeEntry() {
        val attempt = model.begin(pairingCodeProvided = false)
        assertTrue(model.error(attempt, "host-identity", "certificate changed"))
        val snapshot = model.snapshot()
        assertEquals(NxRecovery.Rescan, snapshot.failure?.recovery)
        assertFalse(snapshot.canRetry)
        assertFalse(snapshot.canPairAgain)
    }

    @Test
    fun retryableHostErrorsRemainExplicit() {
        listOf("locked", "replaced", "disconnected", "link-error").forEach { code ->
            val attempt = model.begin(pairingCodeProvided = false)
            assertTrue(model.error(attempt, code))
            assertTrue(model.snapshot().canRetry)
            assertEquals(NxRecovery.Retry, model.snapshot().failure?.recovery)
        }
    }

    @Test
    fun closeAfterStreamingShowsDisconnectedWhileCloseAfterErrorPreservesError() {
        val disconnectedAttempt = model.begin(pairingCodeProvided = false)
        model.streaming(disconnectedAttempt)
        assertTrue(model.closed(disconnectedAttempt))
        assertEquals(NxConnectionPhase.Disconnected, model.snapshot().phase)
        assertTrue(model.snapshot().canRetry)

        val failedAttempt = model.begin(pairingCodeProvided = false)
        model.error(failedAttempt, "bad-code")
        assertFalse(model.closed(failedAttempt))
        assertEquals("bad-code", model.snapshot().failure?.code)
    }

    @Test
    fun closeBeforeStreamingBecomesRecoverableFailure() {
        val attempt = model.begin(pairingCodeProvided = false)
        assertTrue(model.closed(attempt))
        assertEquals("disconnected", model.snapshot().failure?.code)
        assertTrue(model.snapshot().canRetry)
    }

    @Test
    fun backgroundReleasesStateAndRequiresExplicitRetry() {
        val attempt = model.begin(pairingCodeProvided = false)
        model.streaming(attempt)
        assertTrue(model.background(attempt))
        val snapshot = model.snapshot()
        assertEquals(NxConnectionPhase.Background, snapshot.phase)
        assertEquals(null, snapshot.failure)
        assertTrue(snapshot.canRetry)
        assertFalse(model.error(attempt, "disconnected"))
        assertFalse(model.state(attempt, "closed"))
    }

    @Test
    fun duplicateErrorDoesNotReplaceTheFirstActionableCode() {
        val attempt = model.begin(pairingCodeProvided = true)
        assertTrue(model.error(attempt, "host-identity"))
        assertFalse(model.error(attempt, "disconnected"))
        assertEquals("host-identity", model.snapshot().failure?.code)
        assertFalse(model.snapshot().canRetry)
    }
}