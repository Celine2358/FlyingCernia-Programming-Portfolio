using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 맵 1, 2, 3의 순서와 현재 맵 설정,
/// 거리 누적, 배경 교체와 전환 페이드를 관리한다.
/// </summary>
[DefaultExecutionOrder(-100)]
public class MapSequenceController : MonoBehaviour
{
    [Header("맵 진행 순서")]
    public CerniaFlyingMap[] mapSequence;

    [Header("맵 오브젝트 패턴")]
    public MapObjectPatternSpawner mapObjectPatternSpawner;

    [Header("구름 배치")]
    public CloudSpawner cloudSpawner;

    [Header("실제 비행 거리")]
    public CerniaMapDistance mapDistance;

    [Header("월드 참조")]
    // 새로운 배경 스트립
    public BackgroundStrip backgroundStrip;
    // 기존 단일 배경
    public SpriteRenderer mapRenderer;
    public Transform startPoint;
    public Transform endPoint;

    // 셀리느
    public Celine celine;
    public EndTrigger endTrigger;

    [Header("카메라")]
    public CinemachineVirtualCamera virtualCamera;

    [Header("배경 경계")]
    public CameraBorder cameraBorder;

    [Header("맵 컨트롤러")]
    public CerniaCloudRoadController roadController;
    public GameObject shineObject;
    public CanvasGroup shineCanvasGroup;

    [Header("바람 연출")]
    public GameObject windEffect;
    public ParticleSystem windParticle;

    public float fadeInDuration = 0.35f;
    public float coveredHoldDuration = 0.12f;
    public float fadeOutDuration = 0.45f;

    private int currentMapIndex;

    private float completedDistanceMeters;

    // 점수는 내부적으로 float로 누적하고
    // UI에 보여줄 때만 정수로 내린다.
    private float completedDistancePointFloat;

    // 현재 맵에서 아직 확정되지 않은 거리 점수
    private float currentSegmentDistancePointFloat;

    // 마지막으로 점수 계산을 끝낸 현재 맵 거리
    private float lastScoredSegmentMeters;

    // 현재 구간을 completed에 이미 넣었는가?
    private bool currentSegmentAccounted;

    private float segmentStartX;
    private float segmentEndX;
    private float segmentDirection;
    private float segmentWorldLength;

    private bool courseActive;
    private bool isTransitioning;

    private Sequence transitionSequence;

    public bool IsTransitioning => isTransitioning;
    public int CurrentMapIndex => currentMapIndex;

    public CerniaFlyingMap CurrentMapSettings
    {
        get
        {
            if (mapSequence == null || currentMapIndex < 0 || currentMapIndex >= mapSequence.Length)
            {
                return null;
            }

            return mapSequence[currentMapIndex];
        }
    }

    /// <summary>
    /// 현재 구간에서 이동한 월드 거리.
    /// EndPoint가 왼쪽에 배치된 경우에도 대응한다.
    /// </summary>
    public float CurrentSegmentWorldUnits
    {
        get
        {
            if (celine == null) return 0f;

            float traveled = (celine.transform.position.x - segmentStartX) * segmentDirection;
            return Mathf.Clamp(traveled, 0f, segmentWorldLength);
        }
    }

    public float CurrentSegmentDistanceMeters
    {
        get
        {
            CerniaFlyingMap map = CurrentMapSettings;
            if (map == null) return 0f;

            return CurrentSegmentWorldUnits * map.metersPerWorldUnit;
        }
    }

    public float CurrentSegmentTotalMeters
    {
        get
        {
            CerniaFlyingMap map = CurrentMapSettings;
            if (map == null) return 0f;

            return segmentWorldLength * map.metersPerWorldUnit;
        }
    }

    public float TotalDistanceMeters => completedDistanceMeters + (currentSegmentAccounted ? 0f : CurrentSegmentDistanceMeters);

    public int CurrentDistancePoint => Mathf.FloorToInt(completedDistancePointFloat + (currentSegmentAccounted ? 0f : currentSegmentDistancePointFloat));

    void Awake()
    {
        ApplySelectedCourse();
        ValidateReferences();

        currentMapIndex = 0;
        completedDistanceMeters = 0f;
        completedDistancePointFloat = 0f;

        currentSegmentDistancePointFloat = 0f;
        lastScoredSegmentMeters = 0f;
        currentSegmentAccounted = false;

        ApplyCurrentMap(teleportCeline: true);

        if (shineCanvasGroup != null) shineCanvasGroup.blocksRaycasts = true;
    }

    void Update()
    {
        if (!courseActive || isTransitioning || currentSegmentAccounted)
        {
            return;
        }
        AccumulateDistanceScore(CurrentSegmentDistanceMeters);
    }

    /// <summary>
    /// 현재 맵의 특정 거리까지 새로 이동한 부분만 점수화한다.
    ///
    /// Power Fly가 켜졌다 꺼져도
    /// 이미 지나간 거리 점수는 다시 계산하지 않는다.
    /// </summary>
    void AccumulateDistanceScore(float targetMeters)
    {
        CerniaFlyingMap map = CurrentMapSettings;

        if (map == null || celine == null)
        {
            return;
        }

        float currentMeters = Mathf.Clamp(targetMeters, 0f, CurrentSegmentTotalMeters);
        float deltaMeters = currentMeters - lastScoredSegmentMeters;

        if (deltaMeters <= 0f)
        {
            return;
        }

        // 일반 성장 속도 x 현재 Power Fly 속도
        float runtimeSpeedMultiplier = celine.Stats.flightSpeedMultiplier * (celine.IsPowerFlying ? celine.PowerFlySpeedMultiplier : 1f);

        // 속도가 빨라진 만큼 거리 점수에도 보너스
        float scoreMultiplier = celine.Stats.CalculateSpeedScoreMultiplier(runtimeSpeedMultiplier);

        float gainedPoint = deltaMeters * map.pointPerMeter * scoreMultiplier;

        currentSegmentDistancePointFloat += gainedPoint;
        lastScoredSegmentMeters = currentMeters;
    }

    // START! 연출이 끝난 시점에 호출한다.
    public void BeginCourse()
    {
        courseActive = true;
        endTrigger?.Arm();
    }

    // EndPoint Trigger가 호출한다.
    public void NotifyEndPointReached(Celine reachedCeline)
    {
        if (!courseActive || isTransitioning || reachedCeline != celine) return;

        if (currentMapIndex >= mapSequence.Length - 1)
        {
            CompleteFinalMap();
            return;
        }

        PlayNextMapTransition();
    }

    void PlayNextMapTransition()
    {
        isTransitioning = true;
        courseActive = false;

        roadController?.SetMapTransitioning(true);

        celine?.BeginMapTransition();
        endTrigger?.Disarm();

        // Trigger 폭에 따라 약간 일찍 닿아도
        // 완료된 구간은 정확한 전체 길이로 누적한다.
        CompleteCurrentSegmentAccounting(forceFullDistance: true);

        if (shineObject != null)
        {
            shineObject.SetActive(true);
        }

        if (shineCanvasGroup != null)
        {
            shineCanvasGroup.alpha = 0f;
        }

        transitionSequence?.Kill();

        transitionSequence = DOTween.Sequence().SetUpdate(true);

        if (shineCanvasGroup != null)
        {
            transitionSequence.Append(shineCanvasGroup.DOFade(1f, fadeInDuration).SetEase(Ease.InOutSine));
        }
        else
        {
            transitionSequence.AppendInterval(fadeInDuration);
        }

        transitionSequence.AppendCallback(() =>
        {
            currentMapIndex++;
            ApplyCurrentMap(teleportCeline: true);
        });

        transitionSequence.AppendInterval(coveredHoldDuration);

        if (shineCanvasGroup != null)
        {
            transitionSequence.Append(shineCanvasGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.InOutSine));
        }

        transitionSequence.AppendCallback(() =>
        {
            if (shineObject != null)
            {
                shineObject.SetActive(false);
            }

            // 새 맵이 화면에 완전히 나타난 순간
            // 현재 CerniaFlyingMap의 이름을 표시한다.
            roadController?.PlayMapDescription(CurrentMapSettings);

            isTransitioning = false;
            courseActive = true;

            celine?.ResumeAfterMapTransition();
            roadController?.SetMapTransitioning(false);
            endTrigger?.Arm();
        });
    }

    void ApplyCurrentMap(bool teleportCeline)
    {
        CerniaFlyingMap map = CurrentMapSettings;

        if (map == null)
        {
            Debug.LogError($"맵 배열 {currentMapIndex}번이 비어 있습니다.");
            return;
        }

        // 배경부터 교체한다.
        if (backgroundStrip != null)
        {
            backgroundStrip.ApplySprite(map.mapSprite);
        }
        else if (mapRenderer != null && map.mapSprite != null)
        {
            mapRenderer.sprite = map.mapSprite;
        }

        // 새 배경 전체 크기로 Cinemachine 경계를 갱신한다.
        cameraBorder?.FitBorderToMap();

        // 거리 계산기가 현재 맵 데이터를 사용하도록 변경한다.
        if (mapDistance != null)
        {
            mapDistance.mapSettings = map;
        }

        // StartPoint부터 EndPoint 접촉 지점까지의 실제 길이를 계산한다.
        RecalculateSegment();

        // 새 맵의 거리 점수 적분 시작
        currentSegmentDistancePointFloat = 0f;
        lastScoredSegmentMeters = 0f;
        currentSegmentAccounted = false;

        // 셀리느의 물리와 전진 속도를 새 맵에 맞춘다.
        celine?.SetMapSettings(map, segmentWorldLength);

        // 반드시 구름 생성보다 먼저 셀리느를 순간이동시킨다.
        if (teleportCeline) TeleportCelineToStart();

        // 이제 새 시작점 주변에 구름과 패턴을 생성한다.
        cloudSpawner?.Generate(map, currentMapIndex);
        mapObjectPatternSpawner?.Generate(map);

        // 바람이 존재하는 맵에 진입했다면 한 번 재생한다.
        if (map.windInterference > 0f)
        {
            if (windEffect != null)
            {
                windEffect.SetActive(true);
            }

            if (windParticle != null)
            {
                windParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                windParticle.Play(true);
            }
            SoundManager.Instance?.PlaySoundEffect(5);
        }
        else
        {
            if (windParticle != null)
            {
                windParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (windEffect != null)
            {
                windEffect.SetActive(false);
            }
        }
    }

    void TeleportCelineToStart()
    {
        if (celine == null || startPoint == null)
        {
            return;
        }

        Vector3 oldPosition = celine.transform.position;
        Vector2 newPosition = startPoint.position;

        celine.TeleportTo(newPosition);

        Vector3 positionDelta = celine.transform.position - oldPosition;

        if (virtualCamera != null)
        {
            virtualCamera.OnTargetObjectWarped(celine.transform, positionDelta);
        }
    }

    void RecalculateSegment()
    {
        if (startPoint == null || endPoint == null) return;

        segmentStartX = startPoint.position.x;
        segmentEndX = endPoint.position.x;

        float difference = segmentEndX - segmentStartX;

        segmentDirection = Mathf.Approximately(difference, 0f) ? 1f : Mathf.Sign(difference);

        if (mapDistance != null && mapDistance.ContactWorldDistance > 0f)
        {
            segmentWorldLength = mapDistance.ContactWorldDistance;
        }
        else
        {
            segmentWorldLength = Mathf.Abs(difference);
        }
    }

    /// <summary>
    /// 현재 구간의 거리와 점수를 확정한다.
    ///
    /// forceFullDistance = true
    /// 중간 맵 전환용.
    /// Trigger 폭 때문에 조금 일찍 닿아도
    /// 맵 전체 길이를 완료한 것으로 처리한다.
    ///
    /// forceFullDistance = false
    /// 최종 Result용.
    /// 실제 EndPoint 접촉 순간의 위치까지만 확정한다.
    /// </summary>
    void CompleteCurrentSegmentAccounting(bool forceFullDistance)
    {
        if (currentSegmentAccounted)
        {
            return;
        }

        CerniaFlyingMap map = CurrentMapSettings;

        if (map == null)
        {
            return;
        }

        float targetDistance = forceFullDistance ? CurrentSegmentTotalMeters : CurrentSegmentDistanceMeters;

        // Update보다 Trigger가 먼저 실행된 경우에도
        // EndPoint 접촉 순간까지의 거리 점수를 여기서 마지막으로 적분한다.
        AccumulateDistanceScore(targetDistance);

        completedDistanceMeters += targetDistance;
        completedDistancePointFloat += currentSegmentDistancePointFloat;

        currentSegmentDistancePointFloat = 0f;
        lastScoredSegmentMeters = 0f;

        currentSegmentAccounted = true;
    }

    public void StopCourseAtCurrentPosition()
    {
        // 이 순간부터 더 이상 거리와 점수를 적분하지 않는다.
        courseActive = false;
        isTransitioning = false;

        // 혹시 남아 있는 맵 전환 Tween이 있다면 종료
        transitionSequence?.Kill();

        // 현재 셀리느 위치까지만 정확하게 확정
        // 절대로 전체 맵 거리까지 보정 X
        CompleteCurrentSegmentAccounting(forceFullDistance: false);

        // EndPoint가 이후 다시 반응하지 않도록 한다.
        endTrigger?.Disarm();
    }

    // 최종 맵 비행 완료!
    void CompleteFinalMap()
    {
        // 더 이상 거리를 누적하지 않는다.
        courseActive = false;
        isTransitioning = false;

        // 마지막 맵은 실제 EndPoint 접촉 위치까지 확정.
        CompleteCurrentSegmentAccounting(forceFullDistance: false);

        endTrigger?.Disarm();

        // 이제 RoadController가 Result 상태로 전환한다.
        roadController?.CompleteCourseFromMapSequence();
    }

    /// <summary>
    /// MainScreen에서 선택한 Difficulty의
    /// CerniaFlyingMap[]을 받아온다.
    ///
    /// 선택 데이터가 없으면
    /// Inspector의 기존 배열을 그대로 사용한다.
    /// </summary>
    void ApplySelectedCourse()
    {
        CerniaFlightCourseDifficulty difficulty = FlyingCourseSession.CurrentDifficulty;

        if (difficulty == null)
        {
            return;
        }

        CerniaFlyingMap[] selectedMaps = difficulty.GetMapSequence();

        if (selectedMaps == null || selectedMaps.Length == 0)
        {
            Debug.LogWarning("선택한 비행 난이도의 Map Sequence가 비어 있습니다.");
            return;
        }

        mapSequence = selectedMaps;
    }

    public int TotalSectionCount => mapSequence != null ? mapSequence.Length : 0;

    void ValidateReferences()
    {
        if (mapSequence == null || mapSequence.Length == 0)
        {
            Debug.LogError("Cernia 맵 진행 배열이 비어 있습니다.");
        }

        if (startPoint == null || endPoint == null)
        {
            Debug.LogError("StartPoint 또는 EndPoint가 연결되지 않았습니다.");
        }
    }

    void OnDestroy()
    {
        transitionSequence?.Kill();
    }
}