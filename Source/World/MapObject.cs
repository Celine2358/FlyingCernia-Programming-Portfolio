using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 마력 결정, 코인, 회복 아이템, 장애물의
/// Collider 접촉과 자석 이동을 담당한다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class MapObject : MonoBehaviour
{
    [Header("오브젝트 데이터")]
    public MapObjectData data;
    private CerniaCloudRoadController roadController;

    [Header("컴포넌트")]
    public Rigidbody2D body;
    public Collider2D contactCollider;
    public MapObjectComboPopup comboPopup;

    [Header("파괴 연출")]
    public MapObjectBreakEffect breakEffect;

    [Header("해결 후 처리")]
    public bool destroyOnResolve;

    private CelineMagnet magnetSource;
    private float nextContactTime;
    private bool consumed;
    private bool missed;
    private bool enteredPlayableRange;

    // 파괴 점수는 오브젝트 하나당 딱 한 번만.
    private bool breakScoreAwarded;

    public MapObjectData Data => data;
    public bool IsConsumed => consumed;
    public bool IsMissed => missed;
    private bool removed;
    public bool IsResolved => consumed || removed;
    public bool HasEnteredPlayableRange => enteredPlayableRange;
    public bool IsComboCollectible =>
        data != null &&
        data.kind == MapObjectKind.Collectible &&
        data.countsForCombo;

    public bool ResetsComboWhenMissed =>
        IsComboCollectible &&
        data.resetsComboWhenMissed;

    public bool IsBeingAttracted => magnetSource != null && magnetSource.IsActive;
    static readonly HashSet<MapObject> activeWhiteChainTargets = new HashSet<MapObject>();
    public bool CanWhiteChainTarget => !IsResolved && data != null && data.kind == MapObjectKind.QTEObstacle && data.requiresWhiteChain;

    void Awake()
    {
        CacheComponents();
        ConfigurePhysics();
    }

    void OnEnable()
    {
        if (CanWhiteChainTarget)
        {
            activeWhiteChainTargets.Add(this);
        }
    }

    void OnDisable()
    {
        activeWhiteChainTargets.Remove(this);
    }


    /// <summary>
    /// 새 패턴이 생성될 때 단 한 번 호출한다.
    /// 활성화될 때마다 상태를 초기화하지 않는다.
    /// </summary>
    public void PrepareForRun(CerniaCloudRoadController road)
    {
        CacheComponents();

        roadController = road;

        consumed = false;
        missed = false;
        removed = false;
        enteredPlayableRange = false;

        // 새 런에서는 파괴 점수 지급 기록도 초기화.
        breakScoreAwarded = false;

        magnetSource = null;
        nextContactTime = 0f;

        if (contactCollider != null)
        {
            contactCollider.enabled = true;
        }

        if (body != null)
        {
            body.velocity = Vector2.zero;
        }

        ConfigurePhysics();
    }

    public void MarkEnteredPlayableRange()
    {
        enteredPlayableRange = true;
    }

    public void MarkMissed()
    {
        if (IsResolved || missed) return;

        missed = true;
        magnetSource = null;

        // 미스 후 다시 자석이나 충돌로 먹지 못하게 한다.
        if (contactCollider != null)
        {
            contactCollider.enabled = false;
        }
    }

    public void DespawnAfterPass()
    {
        if (IsResolved) return;

        removed = true;
        FinishResolution();
    }

    public void ShowComboMultiplier(float multiplier)
    {
        comboPopup?.Play(multiplier);
    }

    void FixedUpdate()
    {
        if (magnetSource == null || body == null)
        {
            return;
        }

        if (!magnetSource.IsActive || data == null || !data.magnetAttractable)
        {
            magnetSource = null;
            return;
        }

        Vector2 target = magnetSource.transform.position;
        float speed = data.magnetPullSpeed * magnetSource.PullSpeedMultiplier;

        Vector2 next = Vector2.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);

        body.MovePosition(next);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsResolved || data == null)
        {
            return;
        }

        if (!EnsureRoadController())
        {
            return;
        }

        if (Time.time < nextContactTime)
        {
            return;
        }

        Celine reachedCeline = other.GetComponentInParent<Celine>();

        if (reachedCeline == null)
        {
            return;
        }

        // 오브젝트가 비활성화되기 전에 월드 위치를 기억한다.
        Vector3 effectPosition = contactCollider != null ? contactCollider.bounds.center : transform.position;

        bool processed = roadController.TryApplyMapObject(this, reachedCeline);

        if (!processed)
        {
            return;
        }

        // RoadController가 Power Fly 파괴를 처리했다면
        // 일반 Consume 처리를 다시 하지 않는다.
        if (IsResolved)
        {
            return;
        }

        // 획득 이펙트는 수집물과 파워업에만 재생한다.
        bool isPickup = data.kind == MapObjectKind.Collectible || data.kind == MapObjectKind.PowerUp;

        if (isPickup)
        {
            PlayPickupEffect(effectPosition);
        }

        if (ShouldNormalContact())
        {
            Consume();
        }
        else
        {
            // 구름바위와 탄막은 일반 피격으로 제거하지 않는다.
            nextContactTime = Time.time + Mathf.Max(0f, data.contactCooldown);
        }
    }

    bool ShouldNormalContact()
    {
        if (data == null)
        {
            return false;
        }

        if (data.kind == MapObjectKind.Obstacle || data.kind == MapObjectKind.QTEObstacle)
        {
            return data.removeObstacleNormalHit;
        }

        return data.consumeOnContact;
    }

    public void BeginAttraction(CelineMagnet source)
    {
        if (IsResolved || missed || data == null || !data.magnetAttractable)
        {
            return;
        }

        magnetSource = source;
    }

    void Consume()
    {
        consumed = true;
        magnetSource = null;

        if (contactCollider != null)
        {
            contactCollider.enabled = false;
        }

        if (body != null)
        {
            body.velocity = Vector2.zero;
        }

        FinishResolution();
    }

    void CacheComponents()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (contactCollider == null)
        {
            contactCollider = GetComponent<Collider2D>();
        }

        if (comboPopup == null)
        {
            comboPopup = GetComponent<MapObjectComboPopup>();
        }

        if (breakEffect == null)
        {
            breakEffect = GetComponent<MapObjectBreakEffect>();
        }
    }

    void ConfigurePhysics()
    {
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            bool fastObstacle = data != null && (data.kind == MapObjectKind.Obstacle || data.kind == MapObjectKind.QTEObstacle);

            body.collisionDetectionMode = fastObstacle ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        if (contactCollider != null)
        {
            contactCollider.isTrigger = true;
        }
    }

    // 패턴 생성기가 아닌 직접 배치된 테스트 오브젝트 실험용
    bool EnsureRoadController()
    {
        if (roadController != null) return true;

        roadController = FindObjectOfType<CerniaCloudRoadController>();

        if (roadController == null)
        {
            Debug.LogError($"{name}: CerniaCloudRoadController를 찾지 못했습니다.");

            return false;
        }

        Debug.LogWarning($"{name}: PrepareForRun을 거치지 않아 씬에서 CerniaCloudRoadController를 직접 찾았습니다.");
        return true;
    }

    void PlayPickupEffect(Vector3 worldPosition)
    {
        if (data == null || data.pickupEffectPrefab == null) return;

        Vector3 spawnPosition =
        worldPosition + data.pickupEffectOffset;

        GameObject effectInstance = Instantiate(data.pickupEffectPrefab, spawnPosition, data.pickupEffectPrefab.transform.rotation);
        effectInstance.transform.localScale *= data.pickupEffectScale;
    }

    public bool BreakObstacle(GameplayFeedback feedback, int overrideSoundIndex = -1, bool playSound = true, bool playFeedback = true, bool awardScore = true)
    {
        if (IsResolved || data == null)
        {
            return false;
        }

        consumed = true;
        magnetSource = null;

        // Power Fly / White Chain 파괴 점수
        // IsResolved 검사 뒤에 있으므로 한 장애물에서 한 번만 지급된다.
        if (awardScore)
        {
            if (roadController != null || EnsureRoadController())
            {
                roadController?.RegisterBrokenMapObjectScore(this);
            }
        }

        Vector3 breakPosition = contactCollider != null ? contactCollider.bounds.center : transform.position;

        if (contactCollider != null)
        {
            contactCollider.enabled = false;
        }

        if (body != null)
        {
            body.velocity = Vector2.zero;
        }

        int soundIndex = overrideSoundIndex >= 0 ? overrideSoundIndex : data.powerFlyBreakSoundIndex;

        if (playSound && soundIndex >= 0)
        {
            SoundManager.Instance?.PlaySoundEffect(soundIndex);
        }

        // 화면 흔들림과 옵션 기반 모바일 진동
        if (playFeedback)
        {
            if (feedback != null)
            {
                feedback.PlayObstacleBreakFeedback(breakPosition);
            }
            else
            {
                CelineRuntimeOptions.TryVibrate();
            }
        }

        breakEffect?.PlayBreak();

        // Fragment는 부모에서 분리되었으므로
        // 장애물 본체는 즉시 정리해도 된다.
        FinishResolution();
        return true;
    }

    void FinishResolution()
    {
        if (destroyOnResolve)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public static MapObject FindBestWhiteChainTarget(Vector3 celinePosition, float maxAbsX)
    {
        MapObject best = null;
        float bestScore = float.MaxValue;

        foreach (MapObject target in activeWhiteChainTargets)
        {
            if (target == null || !target.gameObject.activeInHierarchy || !target.CanWhiteChainTarget)
            {
                continue;
            }

            Vector3 difference = target.transform.position - celinePosition;
            float absX = Mathf.Abs(difference.x);

            if (absX > maxAbsX)
            {
                continue;
            }

            // 가장 X축으로 가까운 대상을 우선.
            // Y는 동률 보정 정도만 사용한다.
            float score = absX + Mathf.Abs(difference.y) * 0.05f;

            if (score < bestScore)
            {
                bestScore = score;
                best = target;
            }
        }

        return best;
    }

    /// <summary>
    /// 파괴 점수 지급 권한을 한 번만 획득한다.
    ///
    /// 처음 호출: false -> true 후 true 반환
    /// 두 번째 이후: 이미 지급됐으므로 false 반환
    /// </summary>
    public bool TryClaimBreakScoreReward()
    {
        if (breakScoreAwarded)
        {
            return false;
        }

        breakScoreAwarded = true;
        return true;
    }
}