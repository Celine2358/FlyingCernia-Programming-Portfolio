using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Spine.Unity;
using Spine;

/// <summary>
/// Celine.cs
/// 셀리느의 모든 법칙을 구현하는 스크립트.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public partial class Celine : MonoBehaviour
{
    private CerniaFlyingMap mapSettings;

    [Header("셀리느 능력치")]
    [SerializeField] private CelineStats stats = new CelineStats();

    [Header("셀리느의 컴포넌트")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private SkeletonAnimation skeletonAnimation;
    [SerializeField] private Transform visualRoot;

    [Header("파워 플라이")]
    public float powerFlySpeedMultiplier = 3.6f;
    public float PowerFlySpeedMultiplier => powerFlySpeedMultiplier;

    [Header("Input System")]
    [SerializeField] private InputActionReference riseAction;

    [Header("Spine 애니메이션")]
    [SpineAnimation] public string idleAnimationName = "Idle2";
    [SpineAnimation] public string flyingAnimationName = "Flying";
    [SpineAnimation] public string hitAnimationName = "Hit1";
    [SpineAnimation] public string fallingAnimationName = "Falling";
    [SpineAnimation] public string magicalAnimationName = "Magical";
    [SpineAnimation] public string powerFlyingAnimationName = "PowerFlying";

    [Header("자석 능력")]
    [SerializeField]
    private CelineMagnet magnet;
    public CelineMagnet Magnet => magnet;

    [Header("비행 사운드")]
    public int normalFlightSoundIndex = 4;
    public int riseFlightSoundIndex = 6;

    [Header("개발용 셀리느 세팅")]
    [SerializeField] private bool developerHitBox = false;

    public bool ShouldShowHitBox
    {
        get
        {
            return developerHitBox;
        }
    }

    private bool isFlightActive;
    private bool isControlLocked = true;
    private bool riseHeld;

    private float smoothedRiseInput;

    private Quaternion visualInitialRotation;

    private string currentAnimationName;
    private int currentFlightSoundIndex = -1;

    public CelineStats Stats => stats;

    public bool IsFlightActive => isFlightActive;
    public bool IsRising => riseHeld && isFlightActive && !isControlLocked;
    public float VerticalVelocity => body != null ? body.velocity.y : 0f;


    // 목표시간 기반 속도를 사용하나?
    float currentSegmentWorldDistance = 1f;

    // MapController가 현재 구간의 설정을 전달한다.
    public void SetMapSettings(CerniaFlyingMap settings, float segmentWorldDistance)
    {
        mapSettings = settings;
        currentSegmentWorldDistance = Mathf.Max(0.01f, segmentWorldDistance);
    }

    // 기존 호출 호환용 마이그레이션
    public void SetMapSettings(CerniaFlyingMap settings)
    {
        SetMapSettings(settings, 1f);
    }

    /// <summary>
    /// 성장 속도 + 현재 Power Fly까지 반영한
    /// 실제 런타임 비행 속도 배율
    /// </summary>
    public float RuntimeFlightSpeedMultiplier => stats.flightSpeedMultiplier * (isPowerFlying ? powerFlySpeedMultiplier : 1f);

    void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (visualRoot != null)
        {
            visualInitialRotation = visualRoot.localRotation;
        }

        if (magnet == null)
        {
            magnet = GetComponent<CelineMagnet>();
        }
        InitializeHitboxes();
        stats.ResetForRun();
        ConfigureRigidbody();
        PrepareIdle();
    }

    void OnEnable()
    {
        if (riseAction == null)
        {
            return;
        }

        riseAction.action.started += OnRiseStarted;
        riseAction.action.canceled += OnRiseCanceled;
        riseAction.action.Enable();
    }

    void OnDisable()
    {
        if (riseAction != null)
        {
            riseAction.action.started -= OnRiseStarted;
            riseAction.action.canceled -= OnRiseCanceled;
            riseAction.action.Disable();
        }

        StopFlightSound();
    }

    void FixedUpdate()
    {
        // Falling 동안에는 비행 공식을 사용하지 않는다.
        // Partial에서 설정한 Unity 중력이 대신 작동한다.
        if (isFalling)
        {
            return;
        }

        if (!isFlightActive || isControlLocked || mapSettings == null || body == null)
        {
            return;
        }

        // 피격 직후 0.6초 동안
        // 전진과 상승·하강을 모두 정지한다.
        if (isHitStunned)
        {
            body.velocity = Vector2.zero;
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        float targetInput = riseHeld ? 1f : 0f;

        // 상승 입력을 즉시 0 -> 1로 바꾸지 않고,
        // 날개가 힘을 받듯 서서히 변화시킨다.
        smoothedRiseInput = Mathf.MoveTowards(smoothedRiseInput, targetInput, mapSettings.inputResponse * deltaTime);

        float verticalVelocity = body.velocity.y;
        // ay = -g + Ar*u - k*vy
        float acceleration =
            -mapSettings.gravityAcceleration +
            mapSettings.riseAcceleration *
            smoothedRiseInput -
            mapSettings.verticalDrag *
            verticalVelocity;

        // v <- v + aΔt
        verticalVelocity += acceleration * deltaTime;
        verticalVelocity = Mathf.Clamp(verticalVelocity, -mapSettings.maxFallSpeed, mapSettings.maxRiseSpeed);

        // 파워 플라이 상태 시 속도 배율 보정
        float runtimeSpeedMultiplier = stats.flightSpeedMultiplier * (isPowerFlying ? powerFlySpeedMultiplier : 1f);

        // 셀리느는 X축으로 계속 전진하고,
        // Y축은 기존 상승·하강 방정식을 사용한다.
        float forwardVelocity = mapSettings.GetForwardWorldSpeed(currentSegmentWorldDistance, runtimeSpeedMultiplier);
        body.velocity = new Vector2(forwardVelocity, verticalVelocity);
    }

    void Update()
    {
        // White Chain은 Time.timeScale = 0 상태.
        // Spine의 Magical 애니메이션만
        // unscaledDeltaTime으로 직접 진행한다.
        if (isWhiteChainCasting && Time.timeScale <= 0f && skeletonAnimation != null)
        {
            skeletonAnimation.Update(Time.unscaledDeltaTime);
        }

        UpdateVisualRotation();
    }

    /// <summary>
    /// 카운트다운 전 Idle2 상태로 준비한다.
    /// Rigidbody2D의 시뮬레이션도 중지한다.
    /// </summary>
    public void PrepareIdle()
    {
        EnsureEffectReferences();
        StopPowerFlyEffects(false);
        particleController?.StopAllEffects();

        ResetSkeletonTint();
        SetFlightHitbox(false);

        isFlightActive = false;
        isControlLocked = true;

        riseHeld = false;
        smoothedRiseInput = 0f;

        stats.ResetForRun();

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = false;
        }

        if (visualRoot != null)
        {
            visualRoot.localRotation = visualInitialRotation;
        }

        StopFlightSound();
        PlayAnimation(idleAnimationName, true);
    }

    /// <summary>
    /// START! 연출이 끝난 뒤 실제 비행을 시작한다.
    /// </summary>
    public void BeginFlight()
    {
        ResetSkeletonTint();
        riseHeld = false;
        smoothedRiseInput = 0f;

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = true;
        }

        isControlLocked = false;
        isFlightActive = true;

        EnsureEffectReferences();
        particleController?.SetNormalFlight(true);
        PlayAnimation(flyingAnimationName, true);
        PlayFlightSound(normalFlightSoundIndex);
    }

    public void EndFlight()
    {
        isFlightActive = false;
        isControlLocked = true;

        riseHeld = false;
        smoothedRiseInput = 0f;

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = false;
        }

        StopPowerFlyEffects(false);
        particleController?.StopAllEffects();
        StopFlightSound();
        PlayAnimation(idleAnimationName, true);
    }

    /// <summary>
    /// Pause 상태를 적용하거나 해제한다.
    /// </summary>
    public void SetPaused(bool paused)
    {
        isControlLocked = paused;

        if (paused)
        {
            riseHeld = false;
            smoothedRiseInput = 0f;
            SoundManager.Instance?.PauseLoop(true);
        }
        else if (isFlightActive)
        {
            SoundManager.Instance?.PauseLoop(false);

            if (isPowerFlying)
            {
                // Power Fly 상태 유지
                particleController?.SetPowerFlying(true);
                Magnet?.SetPowerFlyForced(true);

                BeginPowerFlyingAnimation();
                PlayFlightSound(powerFlySoundIndex, true);
            }
            else
            {
                particleController?.SetNormalFlight(true);
                RestoreFlyingAnimation();

                PlayFlightSound(normalFlightSoundIndex, true);
            }
        }
    }

    /// <summary>
    /// 모바일 WingRiseButton이 상승 여부를 전달한다.
    /// </summary>
    public void SetMobileRiseHeld(bool held)
    {
        if (!isFlightActive || isControlLocked || isHitStunned || isFalling)
        {
            riseHeld = false;
            return;
        }

        SetRiseHeld(held);
    }

    void SetRiseHeld(bool held)
    {
        if (riseHeld == held)
        {
            return;
        }

        riseHeld = held;

        // 파워 플라이 중에는 상승 입력만 기록하고
        // 19번 전용 루프 사운드는 유지한다.
        if (isPowerFlying)
        {
            return;
        }

        int targetSound = riseHeld ? riseFlightSoundIndex : normalFlightSoundIndex;
        PlayFlightSound(targetSound);
    }

    void OnRiseStarted(InputAction.CallbackContext context)
    {
        if (!isFlightActive || isControlLocked || isHitStunned || isFalling)
        {
            return;
        }

        SetRiseHeld(true);
    }

    void OnRiseCanceled(InputAction.CallbackContext context)
    {
        SetRiseHeld(false);
    }

    void UpdateVisualRotation()
    {
        if (!isFlightActive ||
                isControlLocked ||
                isHitStunned ||
                isFalling ||
                visualRoot == null ||
                mapSettings == null)
        {
            return;
        }

        float normalizedVelocity = Mathf.InverseLerp(-mapSettings.maxFallSpeed, mapSettings.maxRiseSpeed, VerticalVelocity);
        float targetAngle = Mathf.Lerp(mapSettings.fallAngle, mapSettings.riseAngle, normalizedVelocity);

        Quaternion targetRotation = visualInitialRotation * Quaternion.Euler(0f, 0f, targetAngle);

        // 프레임 수보다 실제 시간에 기반한 지수형 보간 계수다.
        float blend = 1f - Mathf.Exp(-mapSettings.rotationSmoothness * Time.deltaTime);
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetRotation, blend);
    }

    void ConfigureRigidbody()
    {
        if (body == null)
        {
            return;
        }

        body.bodyType = RigidbodyType2D.Dynamic;

        // 중력은 위 공식으로 직접 계산한다.
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;

        body.simulated = false;
    }

    /// <summary>
    /// 맵 전환 페이드 동안 물리와 입력을 잠근다.
    /// HP와 마력은 초기화하지 않는다.
    /// </summary>
    public void BeginMapTransition()
    {
        isControlLocked = true;
        riseHeld = false;
        smoothedRiseInput = 0f;

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = false;
        }

        EnsureEffectReferences();

        // 전환 시 파워 플라이 종료
        StopPowerFlyEffects(false);
        particleController?.SetNormalFlight(false);
        SoundManager.Instance?.PauseLoop(true);
    }

    /// <summary>
    /// 셀리느를 다음 맵의 시작점으로 순간이동시킨다.
    /// 물리가 정지된 상태에서 호출해야 한다.
    /// </summary>
    public void TeleportTo(Vector2 worldPosition)
    {
        if (body != null)
        {
            body.position = worldPosition;
            body.velocity = Vector2.zero;
        }

        transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
    }

    /// <summary>
    /// 맵 전환이 끝난 뒤 비행을 그대로 이어간다.
    /// </summary>
    public void ResumeAfterMapTransition()
    {
        riseHeld = false;
        smoothedRiseInput = 0f;

        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.simulated = true;
        }

        isFlightActive = true;
        isControlLocked = false;

        EnsureEffectReferences();
        particleController?.SetNormalFlight(true);
        SoundManager.Instance?.PauseLoop(false);
        PlayFlightSound(normalFlightSoundIndex, true);
        PlayAnimation(flyingAnimationName, true);
    }

    void PlayAnimation(string animationName, bool loop)
    {
        if (string.IsNullOrWhiteSpace(animationName))
        {
            return;
        }

        // Spine 애니메이션 상태에 맞춰
        // 실제 충돌용 Collider도 함께 전환한다.
        ApplyHitboxForAnimation(animationName);

        if (skeletonAnimation == null || currentAnimationName == animationName)
        {
            return;
        }

        skeletonAnimation.AnimationState.SetAnimation(0, animationName, loop);
        currentAnimationName = animationName;
    }

    /// <summary>
    /// 피격 애니메이션
    /// Small: 피격 연출과 무적만 적용.
    /// Large: 피격 연출, 무적, 0.6초 경직을 적용.
    /// </summary>
    public void PlayHitAnimation(bool applyStun)
    {
        if (!isFlightActive || isControlLocked || IsInvulnerable) return;
        BeginHitReaction(applyStun);
    }

    public void BeginFalling()
    {
        StartFallingState();
    }

    public void BeginMagicalAnimation()
    {
        PlayAnimation(magicalAnimationName, true);
    }

    public void BeginPowerFlyingAnimation()
    {
        PlayAnimation(powerFlyingAnimationName, true);
    }

    public void RestoreFlyingAnimation()
    {
        if (!isFlightActive)
        {
            return;
        }

        PlayAnimation(flyingAnimationName, true);
    }

    void PlayFlightSound(int soundIndex, bool force = false)
    {
        if (!force && currentFlightSoundIndex == soundIndex)
        {
            return;
        }

        if (SoundManager.Instance == null)
        {
            return;
        }

        SoundManager.Instance.PlayLoop(soundIndex);
        currentFlightSoundIndex = soundIndex;
    }

    void StopFlightSound()
    {
        SoundManager.Instance?.StopLoop();
        currentFlightSoundIndex = -1;
    }
}