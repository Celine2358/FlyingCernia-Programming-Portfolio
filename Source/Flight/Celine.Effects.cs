using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Celine.Effects.cs
/// 셀리느의 여러 상태 파티클 연출과 Power Fly등의 상태 담당
/// </summary>
public partial class Celine
{
    [Header("셀리느 상태 연출")]
    public CelineStatusPopup statusPopup;
    public CelineParticleController particleController;

    [Header("POWER FLYING")]
    public int powerFlySoundIndex = 19;

    private Coroutine powerFlyCoroutine;
    private bool isPowerFlying;
    private bool isWhiteChainCasting;

    // White Chain 성공으로 얻은 무료 Power Fly 시간
    private float powerFlyFreeTimeRemaining;

    // Power Fly 연료로 예약된 마력.
    // UI상 마력에는 남아 있지만 다른 White Chain에서는 사용할 수 없다.
    private float reservedPowerFlyMagic;

    // 기본 1 Magic = 0.1초
    private float powerFlySecondsPerMagic = 0.1f;

    public bool IsPowerFlying => isPowerFlying;
    public bool IsWhiteChainCasting => isWhiteChainCasting;

    /// <summary>
    /// 현재 실제로 새로운 White Chain에 사용할 수 있는 마력.
    /// 이미 Power Fly 연료로 예약한 마력은 제외한다.
    /// </summary>
    public float AvailableWhiteChainMagic => Mathf.Max(0f, stats.currentMagic - reservedPowerFlyMagic);

    /// <summary>
    /// 현재 남아 있는 전체 Power Fly 시간.
    /// 무료시간 + 예약 마력이 가진 시간.
    /// </summary>
    public float PowerFlyRemainingTime =>
        Mathf.Max(0f, powerFlyFreeTimeRemaining) +
        Mathf.Max(0f, reservedPowerFlyMagic) *
        Mathf.Max(0.001f, powerFlySecondsPerMagic);

    public void ShowPositiveStatus(string text)
    {
        EnsureEffectReferences();
        statusPopup?.ShowPositive(text);
    }

    public void ShowNegativeStatus(string text)
    {
        EnsureEffectReferences();
        statusPopup?.ShowNegative(text);
    }

    public void ShowSpecialStatus(string text)
    {
        EnsureEffectReferences();
        statusPopup?.ShowSpecial(text);
    }

    public void PlayWhiteChainSuccessEffects()
    {
        EnsureEffectReferences();
        particleController?.PlayWhiteChainSuccess();
    }

    /// <summary>
    /// 이미 Power Fly 연료로 예약된 마력을 제외하고
    /// White Chain 비용을 소비한다.
    /// </summary>
    public bool TryConsumeWhiteChainMagic(float amount)
    {
        float safeAmount = Mathf.Max(0f, amount);

        if (AvailableWhiteChainMagic + 0.001f < safeAmount)
        {
            return false;
        }

        return stats.TryConsumeMagic(safeAmount);
    }

    /// <summary>
    /// 아이템 등으로 단순 Power Fly 시간을 얻는 기존 호환용.
    /// </summary>
    public void BeginPowerFly(float duration)
    {
        BeginOrExtendPowerFly(duration, 0f, 0f, powerFlySecondsPerMagic);
    }

    /// <summary>
    /// White Chain 성공으로 Power Fly를 시작하거나 연장한다.
    ///
    /// 새 총시간 = 기존 남은시간 + 기본시간 + QTE 남은시간 + 예약마력 x 초/Magic
    /// </summary>
    public float BeginOrExtendPowerFly(float baseDuration, float qteTimeBonus, float bonusMagic, float secondsPerMagic)
    {
        if (!isFlightActive || isFalling)
        {
            return PowerFlyRemainingTime;
        }

        EnsureEffectReferences();

        powerFlySecondsPerMagic = Mathf.Max(0.001f, secondsPerMagic);

        // 기존 Power Fly 시간을 없애지 않고 추가한다.
        powerFlyFreeTimeRemaining += Mathf.Max(0f, baseDuration) + Mathf.Max(0f, qteTimeBonus);

        // 아직 다른 Power Fly에 예약되지 않은 마력만 예약한다.
        float reservableMagic = Mathf.Min(Mathf.Max(0f, bonusMagic), AvailableWhiteChainMagic);

        reservedPowerFlyMagic += reservableMagic;

        if (!isPowerFlying)
        {
            isPowerFlying = true;

            particleController?.SetPowerFlying(true);
            Magnet?.SetPowerFlyForced(true);

            BeginPowerFlyingAnimation();
        }

        ShowSpecialStatus("POWER FLY!!");

        // White Chain 7번 루프가 끝난 뒤
        // 다시 19번 Power Fly 루프로 복귀.
        PlayFlightSound(powerFlySoundIndex, true);

        if (powerFlyCoroutine == null)
        {
            powerFlyCoroutine = StartCoroutine(PowerFlyRoutine());
        }

        return PowerFlyRemainingTime;
    }


    IEnumerator PowerFlyRoutine()
    {
        while (isPowerFlying && !isFalling)
        {
            // White Chain 중에는 Time.timeScale = 0이라
            // Time.deltaTime도 0이므로 Power Fly 시간이 멈춘다.
            float dt = Time.deltaTime;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            // 먼저 기본 시간 + QTE 시간 보너스를 사용한다.
            if (powerFlyFreeTimeRemaining > 0f)
            {
                powerFlyFreeTimeRemaining = Mathf.Max(0f, powerFlyFreeTimeRemaining - dt);
            }
            // 무료시간을 모두 사용한 뒤
            // 예약된 마력을 연료처럼 태운다.
            else if (reservedPowerFlyMagic > 0f)
            {
                // 1 Magic = 0.1초라면
                // 초당 10 Magic 소비.
                float magicDrainPerSecond = 1f / powerFlySecondsPerMagic;
                float drain = Mathf.Min(reservedPowerFlyMagic, magicDrainPerSecond * dt);

                reservedPowerFlyMagic -= drain;
                stats.DrainMagic(drain);
            }
            else
            {
                break;
            }
            yield return null;
        }

        FinishPowerFlyNaturally();
    }

    void FinishPowerFlyNaturally()
    {
        isPowerFlying = false;

        powerFlyFreeTimeRemaining = 0f;
        reservedPowerFlyMagic = 0f;

        EnsureEffectReferences();

        particleController?.SetPowerFlying(false);
        Magnet?.SetPowerFlyForced(false);

        if (isFlightActive && !isFalling && !isWhiteChainCasting)
        {
            particleController?.SetNormalFlight(true);
            RestoreFlyingAnimation();

            int flightSound = riseHeld ? riseFlightSoundIndex : normalFlightSoundIndex;

            PlayFlightSound(flightSound, true);
        }
        powerFlyCoroutine = null;
    }

    void StopPowerFlyEffects(bool restoreNormalEffects)
    {
        if (powerFlyCoroutine != null)
        {
            StopCoroutine(powerFlyCoroutine);
            powerFlyCoroutine = null;
        }

        isPowerFlying = false;

        // 강제 중단이라면 아직 소비하지 않은 예약 마력은
        // 실제 Stats에서 빼지 않고 다시 사용 가능 상태로 돌린다.
        powerFlyFreeTimeRemaining = 0f;
        reservedPowerFlyMagic = 0f;

        EnsureEffectReferences();

        particleController?.SetPowerFlying(false);
        Magnet?.SetPowerFlyForced(false);

        if (restoreNormalEffects && isFlightActive && !isFalling)
        {
            particleController?.SetNormalFlight(true);
            RestoreFlyingAnimation();

            PlayFlightSound(normalFlightSoundIndex, true);
        }
    }

    void EnsureEffectReferences()
    {
        if (statusPopup == null)
        {
            statusPopup = GetComponentInChildren<CelineStatusPopup>(true);
        }

        if (particleController == null)
        {
            particleController = GetComponent<CelineParticleController>();
        }
    }

    /// <summary>
    /// White Chain 시작.
    ///
    /// Power Fly 중이라면:
    /// PowerFlying 애니메이션과 파티클은 그대로 유지한다.
    /// 다만 실제 이동은 정지한다.
    /// </summary>
    public void BeginWhiteChainCast()
    {
        if (!isFlightActive || isFalling || isWhiteChainCasting)
        {
            return;
        }

        isWhiteChainCasting = true;
        isControlLocked = true;

        riseHeld = false;
        smoothedRiseInput = 0f;

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = false;
        }

        EnsureEffectReferences();

        // 기존 Flying/PowerFly 루프 사운드를 멈춘다.
        // 이후 WhiteChainController가 7번 루프를 재생한다.
        StopFlightSound();

        if (!isPowerFlying)
        {
            particleController?.SetNormalFlight(false);

            // 일반 비행 중 White Chain만 Magical 애니메이션.
            BeginMagicalAnimation();
        }

        // Power Fly 중이면
        // PowerFlying Animation/Particle은 그대로 둔다.
    }

    public void EndWhiteChainCast(bool restoreNormalFlight)
    {
        isWhiteChainCasting = false;

        if (body != null && !isFalling)
        {
            body.velocity = Vector2.zero;
            body.simulated = true;
        }

        if (!isFlightActive || isFalling)
        {
            return;
        }

        isControlLocked = false;

        // White Chain 이전부터 Power Fly였다면
        // 다시 Power Fly 사운드와 상태를 복구한다.
        if (isPowerFlying)
        {
            particleController?.SetPowerFlying(true);
            Magnet?.SetPowerFlyForced(true);

            BeginPowerFlyingAnimation();
            PlayFlightSound(powerFlySoundIndex, true);

            return;
        }

        if (restoreNormalFlight)
        {
            particleController?.SetNormalFlight(true);
            RestoreFlyingAnimation();
            PlayFlightSound(normalFlightSoundIndex, true);
        }
    }
}