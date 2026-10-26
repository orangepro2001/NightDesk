package dev.nightdesk.android

/** User-visible connection phase. Transport failures stay explicit instead of looking like a healthy session. */
enum class NxConnectionPhase {
    Connecting,
    Streaming,
    Failed,
    Disconnected,
    Background
}

/** The only recovery action the UI should offer for a classified failure. */
enum class NxRecovery {
    Retry,
    PairAgain,
    Rescan,
    None
}

data class NxFailure(val code: String, val detail: String = "") {
    val recovery: NxRecovery
        get() = when (code) {
            "bad-code", "untrusted" -> NxRecovery.PairAgain
            // A changed certificate is a security boundary, not a transient network error.
            "host-identity" -> NxRecovery.Rescan
            // A corrupt local identity cannot be repaired by opening the same host again.
            "identity" -> NxRecovery.None
            else -> NxRecovery.Retry
        }
}

data class NxConnectionSnapshot(
    val attempt: Long = 0,
    val phase: NxConnectionPhase = NxConnectionPhase.Connecting,
    val failure: NxFailure? = null,
    val pairingCodeProvided: Boolean = false,
    val deadlineMillis: Long = 0
) {
    val canRetry: Boolean
        get() = phase in setOf(NxConnectionPhase.Failed, NxConnectionPhase.Disconnected, NxConnectionPhase.Background) &&
            (failure?.recovery ?: NxRecovery.Retry) == NxRecovery.Retry

    val canPairAgain: Boolean
        get() = phase == NxConnectionPhase.Failed && failure?.recovery == NxRecovery.PairAgain
}

/**
 * Pure connection reducer shared by the Activity and JVM tests.
 *
 * Every callback carries its attempt number. Events from an older attempt are ignored so a
 * late QUIC callback cannot overwrite a newer retry or a background release. The model never
 * retries by itself: uncertainty is surfaced as a recoverable state and requires an explicit tap.
 */
class NxConnectionModel(
    private val clock: () -> Long = System::currentTimeMillis
) {
    private var current = NxConnectionSnapshot()

    fun snapshot(): NxConnectionSnapshot = current

    fun begin(pairingCodeProvided: Boolean, timeoutMillis: Long = DefaultTimeoutMillis): Long {
        require(timeoutMillis in 1..MaxTimeoutMillis) { "连接超时时间无效" }
        val next = current.attempt + 1
        current = NxConnectionSnapshot(
            attempt = next,
            phase = NxConnectionPhase.Connecting,
            pairingCodeProvided = pairingCodeProvided,
            deadlineMillis = clock() + timeoutMillis
        )
        return next
    }

    fun isCurrent(attempt: Long): Boolean = current.attempt == attempt

    fun state(attempt: Long, state: String): Boolean {
        if (!isCurrent(attempt)) return false
        return when (state) {
            "connecting" -> {
                if (current.phase != NxConnectionPhase.Connecting) return false
                true
            }
            "streaming" -> streaming(attempt)
            "closed" -> closed(attempt)
            else -> false
        }
    }

    fun streaming(attempt: Long): Boolean {
        if (!isCurrent(attempt) || current.phase != NxConnectionPhase.Connecting) return false
        current = current.copy(phase = NxConnectionPhase.Streaming, failure = null, deadlineMillis = 0)
        return true
    }

    fun error(attempt: Long, code: String, detail: String = ""): Boolean {
        // Preserve the first actionable error. The Go session reports OnError and then OnClosed,
        // and NewSession may also return the same error to the caller.
        if (!isCurrent(attempt) || current.phase == NxConnectionPhase.Failed) return false
        if (current.phase == NxConnectionPhase.Background) return false
        val normalized = code.trim().lowercase().ifBlank { "link-error" }
        current = current.copy(
            phase = NxConnectionPhase.Failed,
            failure = NxFailure(normalized, detail.take(512)),
            deadlineMillis = 0
        )
        return true
    }

    fun closed(attempt: Long): Boolean {
        if (!isCurrent(attempt)) return false
        return when (current.phase) {
            NxConnectionPhase.Connecting -> {
                current = current.copy(
                    phase = NxConnectionPhase.Failed,
                    failure = NxFailure("disconnected", "session closed before the first frame"),
                    deadlineMillis = 0
                )
                true
            }
            NxConnectionPhase.Streaming -> {
                current = current.copy(phase = NxConnectionPhase.Disconnected, deadlineMillis = 0)
                true
            }
            else -> false
        }
    }

    fun timeout(attempt: Long): Boolean {
        if (!isCurrent(attempt) || current.phase != NxConnectionPhase.Connecting) return false
        if (clock() < current.deadlineMillis) return false
        current = current.copy(
            phase = NxConnectionPhase.Failed,
            failure = NxFailure("timeout", "connection attempt exceeded its deadline"),
            deadlineMillis = 0
        )
        return true
    }

    fun background(attempt: Long): Boolean {
        if (!isCurrent(attempt) || current.phase == NxConnectionPhase.Background) return false
        current = current.copy(phase = NxConnectionPhase.Background, failure = null, deadlineMillis = 0)
        return true
    }

    companion object {
        const val DefaultTimeoutMillis = 20_000L
        const val MaxTimeoutMillis = 120_000L
    }
}
