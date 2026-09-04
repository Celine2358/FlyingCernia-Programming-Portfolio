using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum WhiteChainKey
{
    Q, W, E, A, S, D, Z, X, C, One, Two, Three
}

public static class WhiteChainKeyUtility
{
    public static string ToDisplayString(WhiteChainKey key)
    {
        return key switch
        {
            WhiteChainKey.One => "1",
            WhiteChainKey.Two => "2",
            WhiteChainKey.Three => "3",
            _ => key.ToString()
        };
    }
}

/// <summary>
/// 화이트 체인의 탐색, QTE, 성공/실패, Power Fly 연결 담당
/// </summary>
public class WhiteChainController : MonoBehaviour
{
    [Header("게임 참조")]
    public Celine celine;
    public CerniaCloudRoadController roadController;
    public GameplayFeedback gameplayFeedback;

    [Header("Input System")]
    public InputActionReference whiteChainAction;
    public InputActionAsset inputAsset;

    [Header("화이트 체인 UI")]
    public GameObject whiteChainShadow;
    public Image whiteChainShadowImage;
    public CanvasGroup whiteChainShadowCanvasGroup;

    [Header("모바일 화이트 체인")]
    public Camera worldCamera;
    public InputActionReference pointerPressAction;
    public InputActionReference pointerPositionAction;
    public LayerMask whiteChainTouchLayer;

    [Tooltip("손가락 터치 오차 허용 월드 반경")]
    public float mobileTouchRadius = 0.4f;

    [Tooltip("에디터에서 마우스로 Mobile 입력 테스트")]
    public bool previewMobileInEditor = false;

    [Header("사용 범위")]
    public float targetRangeX = 10f;

    [Header("QTE")]
    public int minimumCoreCount = 3;
    public int maximumCoreCount = 5;

    [Header("파워 플라이")]
    public float basePowerFlyDuration = 3f;

    [Tooltip("남은 마력 1당 추가 파워 플라이 시간")]
    public float remainingMagicBonus = 0.1f;

    [Header("점수")]
    public int baseSuccessScore = 1000;

    [Tooltip("남은 제한시간 0.1초당 기본 점수의 몇 %를 추가할지")]
    public float scorePercentPerTenthSecond = 0.05f;

    [Header("효과음")]
    public int beginSoundIndex = 14;
    public int successSoundIndex = 15;
    // 모든 QTE Core를 해결한 순간의 Clear음
    public int qteClearSoundIndex = 16;
    public int failureSoundIndex = 22;

    [Header("성공 이펙트")]
    public GameObject whiteChainSuccessPrefab;

    private InputActionMap qteActionMap;
    private readonly List<WhiteChainKey> sequence = new List<WhiteChainKey>();
    private readonly List<bool> resolvedCores = new List<bool>();
    private int resolvedCoreCount;
    private MapObject currentTarget;
    private WhiteChainTarget currentTargetView;
    private int currentInputIndex;
    private float timeLimit;
    private float remainingTime;
    private float elapsedTime;
    private bool active;
    public bool IsActive => active;
    public float RemainingTime => remainingTime;

    public bool UseMobileQte
    {
        get
        {
#if UNITY_ANDROID || UNITY_IOS
return true;
#elif UNITY_EDITOR
return previewMobileInEditor;
#else
return false;
#endif
        }
    }

    void Awake()
    {
        if (inputAsset != null)
        {
            qteActionMap = inputAsset.FindActionMap("QTE", false);

            if (qteActionMap != null)
            {
                foreach (InputAction action in qteActionMap.actions)
                {
                    action.performed += OnQtePerformed;
                }
                qteActionMap.Disable();
            }
        }

        if (whiteChainShadow != null) whiteChainShadow.SetActive(false);
    }

    void OnEnable()
    {
        if (whiteChainAction != null)
        {
            whiteChainAction.action.performed += OnWhiteChainPressed;
            whiteChainAction.action.Enable();
        }

        if (pointerPressAction != null)
        {
            pointerPressAction.action.performed += OnPointerPressed;
            pointerPressAction.action.Enable();
        }

        if (pointerPositionAction != null)
        {
            pointerPositionAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (whiteChainAction != null) whiteChainAction.action.performed -= OnWhiteChainPressed;
        if (pointerPressAction != null) pointerPressAction.action.performed -= OnPointerPressed;
        if (qteActionMap != null) qteActionMap.Disable();
        AbortWhiteChainImmediate();
    }

    void Update()
    {
        if (!active) return;

        // timeScale = 0이므로 반드시 unscaledDeltaTime.
        float dt = Time.unscaledDeltaTime;

        elapsedTime += dt;
        remainingTime -= dt;

        currentTargetView?.SetRemainingTime(remainingTime, timeLimit);

        if (remainingTime <= 0f)
        {
            FailWhiteChain();
        }
    }

    void OnWhiteChainPressed(InputAction.CallbackContext context)
    {
        TryBeginWhiteChain();
    }

    void OnPointerPressed(InputAction.CallbackContext context)
    {
        // PC에서는 Shift 방식 사용.
        if (!UseMobileQte || active)
        {
            return;
        }

        if (worldCamera == null || pointerPositionAction == null)
        {
            return;
        }

        Vector2 screenPosition = pointerPositionAction.action.ReadValue<Vector2>();
        Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, Mathf.Abs(worldCamera.transform.position.z));
        Vector2 worldPosition = worldCamera.ScreenToWorldPoint(screenPoint);

        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPosition, mobileTouchRadius, whiteChainTouchLayer);
        MapObject best = null;
        float bestDistance = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            MapObject candidate = hit.GetComponentInParent<MapObject>();

            if (candidate == null || !candidate.CanWhiteChainTarget)
            {
                continue;
            }

            float distance = ((Vector2)candidate.transform.position - worldPosition).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best != null)
        {
            TryBeginWhiteChain(best);
        }
    }

    public bool TryBeginWhiteChain()
    {
        // PC Shift는 가장 적합한 대상을 자동 탐색.
        return TryBeginWhiteChain(null);
    }

    public bool TryBeginWhiteChain(MapObject requestedTarget)
    {
        if (active ||
            celine == null ||
            roadController == null ||
            !roadController.CanStartWhiteChain ||
            celine.IsFalling)
        {
            return false;
        }

        float cost = roadController.CurrentWhiteChainCost;

        // Power Fly 연료로 예약된 마력은 제외.
        if (cost <= 0f || celine.AvailableWhiteChainMagic + 0.001f < cost)
        {
            return false;
        }

        // Mobile은 직접 터치한 대상.
        // PC는 X거리 기준 자동 선택.
        currentTarget = requestedTarget != null ? requestedTarget : MapObject.FindBestWhiteChainTarget(celine.transform.position, targetRangeX);

        if (!IsValidTarget(currentTarget))
        {
            return false;
        }

        currentTargetView = currentTarget.GetComponent<WhiteChainTarget>();

        if (currentTargetView == null)
        {
            Debug.LogWarning($"{currentTarget.name}: WhiteChainTarget가 없습니다.");
            return false;
        }

        // 성공/실패와 관계없이 시작할 때 비용 소비.
        if (!celine.TryConsumeWhiteChainMagic(cost))
        {
            return false;
        }

        if (!roadController.BeginWhiteChainMode())
        {
            // 혹시 상태가 같은 프레임에 바뀌었다면 마력 환불.
            celine.Stats.AddMagic(cost);
            return false;
        }

        int count = Random.Range(Mathf.Min(minimumCoreCount, maximumCoreCount), Mathf.Max(minimumCoreCount, maximumCoreCount) + 1);

        // 3 -> 4초
        // 4 -> 5초
        // 5 -> 6초
        timeLimit = count + 1f;

        remainingTime = timeLimit;
        elapsedTime = 0f;
        currentInputIndex = 0;

        GenerateSequence(count);

        // 각 Core가 해결됐는지 따로 기록.
        // Windows는 순서 자유,
        // Mobile은 currentInputIndex로 순서를 강제한다.
        resolvedCores.Clear();

        for (int i = 0; i < count; i++)
        {
            resolvedCores.Add(false);
        }

        resolvedCoreCount = 0;

        active = true;

        celine.BeginWhiteChainCast();

        ShowShadow();

        currentTargetView.Begin(sequence, timeLimit, UseMobileQte, SubmitMobileCore);

        // 모바일에서는 키보드 QTE Map 불필요.
        if (!UseMobileQte)
        {
            qteActionMap?.Enable();
        }

        // 7번은 이제 일회성 효과음이 아니라
        // White Chain이 끝날 때까지 반복.
        SoundManager.Instance?.PlayLoop(beginSoundIndex);

        return true;
    }

    bool IsValidTarget(MapObject target)
    {
        if (target == null ||
            !target.gameObject.activeInHierarchy ||
            !target.CanWhiteChainTarget)
        {
            return false;
        }

        float absX = Mathf.Abs(target.transform.position.x - celine.transform.position.x);
        return absX <= targetRangeX;
    }

    public void SubmitMobileCore(int pressedIndex)
    {
        if (!active || currentInputIndex >= sequence.Count)
        {
            return;
        }

        // Mobile은 반드시 1 -> 2 -> 3 -> ... 순서.
        if (pressedIndex != currentInputIndex)
        {
            currentTargetView?.PlayWrong(pressedIndex);
            return;
        }

        if (resolvedCores[currentInputIndex])
        {
            return;
        }

        resolvedCores[currentInputIndex] = true;
        resolvedCoreCount++;

        currentTargetView?.ResolveCore(currentInputIndex);

        // QTE Core 터치 성공음
        SoundManager.Instance?.PlaySoundEffect(successSoundIndex);

        currentInputIndex++;

        if (resolvedCoreCount >= sequence.Count)
        {
            CompleteWhiteChain();
        }
    }

    void OnQtePerformed(InputAction.CallbackContext context)
    {
        if (!active) return;

        if (!TryConvertAction(context.action.name, out WhiteChainKey pressed))
        {
            return;
        }
        SubmitKey(pressed);
    }

    /// <summary>
    /// Windows QTE 입력.
    ///
    /// Windows에서는 Core를 어떤 순서로 해결해도 된다.
    /// 같은 키가 여러 개라면 한 번의 입력으로
    /// 아직 해결되지 않은 Core 하나만 처리한다.
    /// </summary>
    public void SubmitKey(WhiteChainKey pressed)
    {
        if (!active)
        {
            return;
        }

        int matchedIndex = -1;

        // 아직 해결되지 않은 Core 중
        // 눌린 키와 같은 첫 번째 Core를 찾는다.
        for (int i = 0; i < sequence.Count; i++)
        {
            if (resolvedCores[i])
            {
                continue;
            }

            if (sequence[i] == pressed)
            {
                matchedIndex = i;
                break;
            }
        }

        // 현재 남아있는 Core 중
        // 해당 키가 하나도 없다.
        if (matchedIndex < 0)
        {
            int wrongIndex = FindFirstUnresolvedCore();

            if (wrongIndex >= 0)
            {
                currentTargetView?.PlayWrong(wrongIndex);
            }

            return;
        }

        // 정확히 Core 하나만 해결.
        resolvedCores[matchedIndex] = true;
        resolvedCoreCount++;

        currentTargetView?.ResolveCore(matchedIndex);

        // QTE Core 올바른 키 성공음
        SoundManager.Instance?.PlaySoundEffect(successSoundIndex);

        if (resolvedCoreCount >= sequence.Count)
        {
            CompleteWhiteChain();
        }
    }

    int FindFirstUnresolvedCore()
    {
        for (int i = 0; i < resolvedCores.Count; i++)
        {
            if (!resolvedCores[i])
            {
                return i;
            }
        }

        return -1;
    }

    void GenerateSequence(int count)
    {
        sequence.Clear();

        WhiteChainKey[] all = (WhiteChainKey[])System.Enum.GetValues(typeof(WhiteChainKey));

        for (int i = 0; i < count; i++)
        {
            sequence.Add(SelectWeightedKey(all));
        }
    }

    WhiteChainKey SelectWeightedKey(WhiteChainKey[] all)
    {
        float totalWeight = 0f;

        float[] weights = new float[all.Length];

        for (int i = 0; i < all.Length; i++)
        {
            float weight = 1f;

            // 이미 나온 키는 다시 등장할 확률 감소.
            if (sequence.Contains(all[i]))
            {
                weight *= 0.35f;
            }

            // 바로 직전 키와 같은 키는 더 강하게 감소.
            if (sequence.Count > 0 && sequence[sequence.Count - 1] == all[i])
            {
                weight *= 0.35f;
            }

            weights[i] = weight;
            totalWeight += weight;
        }

        float random = Random.Range(0f, totalWeight);

        float accumulated = 0f;

        for (int i = 0; i < all.Length; i++)
        {
            accumulated += weights[i];

            if (random <= accumulated)
            {
                return all[i];
            }
        }
        return all[all.Length - 1];
    }

    // 화이트 체인 완료
    void CompleteWhiteChain()
    {
        if (!active) return;

        active = false;

        float remaining = Mathf.Max(0f, remainingTime);
        float clearTime = Mathf.Max(0f, elapsedTime);

        int score = CalculateSuccessScore(remaining);

        qteActionMap?.Disable();

        // White Chain 7번 루프 종료.
        SoundManager.Instance?.StopLoop();

        currentTargetView?.FinishSuccess();
        HideShadowImmediate();

        // 일반 세계 시간 복구.
        roadController.EndWhiteChainMode();

        // Power Fly였다면 PowerFly 상태는 살아 있고,
        // 일반 White Chain이었다면 잠시 Magical 상태 종료.
        celine.EndWhiteChainCast(false);

        Vector3 breakPosition = currentTarget.transform.position;

        currentTarget.BreakObstacle(gameplayFeedback, -1, true, false);
        celine.PlayWhiteChainSuccessEffects();
        SpawnSuccessEffect(breakPosition);

        SoundManager.Instance?.PlaySoundEffect(qteClearSoundIndex);
        gameplayFeedback?.PlayWhiteChainFeedback(breakPosition);

        // 기존 Power Fly 연료로 예약되지 않은
        // 현재 남은 마력만 새 보너스로 사용.
        float bonusMagic = celine.AvailableWhiteChainMagic;

        // 기존 남은 Power Fly에 추가된다.
        float totalPowerFlyRemaining = celine.BeginOrExtendPowerFly(basePowerFlyDuration, remaining, bonusMagic, remainingMagicBonus);

        roadController.RegisterWhiteChainSuccess(score, clearTime, remaining, totalPowerFlyRemaining);
        CleanupReferences();
    }

    void FailWhiteChain()
    {
        if (!active) return;

        active = false;
        qteActionMap?.Disable();

        SoundManager.Instance?.StopLoop();
        currentTargetView?.FinishFailure();
        SoundManager.Instance?.PlaySoundEffect(failureSoundIndex);
        roadController.RegisterWhiteChainFailure(elapsedTime);

        PlayFailureShadow();
    }

    void FinishFailureResume()
    {
        HideShadowImmediate();

        roadController.EndWhiteChainMode();
        celine.EndWhiteChainCast(true);

        CleanupReferences();
    }

    int CalculateSuccessScore(float remaining)
    {
        // 남은 0.1초의 개수.
        float tenthSeconds = remaining / 0.1f;

        // 예: 1초 남음
        // 10 x 5% = +50%
        float bonusMultiplier = 1f + tenthSeconds * scorePercentPerTenthSecond;

        return Mathf.Max(0, Mathf.RoundToInt(baseSuccessScore * bonusMultiplier));
    }

    void ShowShadow()
    {
        if (whiteChainShadow == null)
        {
            return;
        }

        whiteChainShadow.SetActive(true);

        if (whiteChainShadowCanvasGroup != null)
        {
            whiteChainShadowCanvasGroup.alpha = 1f;
        }
    }

    void HideShadowImmediate()
    {
        if (whiteChainShadowCanvasGroup != null)
        {
            whiteChainShadowCanvasGroup.DOKill();
        }

        if (whiteChainShadow != null)
        {
            whiteChainShadow.SetActive(false);
        }
    }

    void PlayFailureShadow()
    {
        if (whiteChainShadowCanvasGroup == null)
        {
            FinishFailureResume();
            return;
        }

        whiteChainShadowCanvasGroup.DOKill();

        // timeScale = 0에서도 재생.
        Sequence sequence = DOTween.Sequence().SetUpdate(true);

        sequence.Append(whiteChainShadowCanvasGroup.DOFade(0.3f, 0.08f));
        sequence.Append(whiteChainShadowCanvasGroup.DOFade(1f, 0.08f));
        sequence.Append(whiteChainShadowCanvasGroup.DOFade(0.25f, 0.08f));

        sequence.AppendCallback(FinishFailureResume);
    }

    void SpawnSuccessEffect(Vector3 position)
    {
        if (whiteChainSuccessPrefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(whiteChainSuccessPrefab, position, Quaternion.identity);
        StartCoroutine(DestroyEffectRealtime(instance));
    }

    IEnumerator DestroyEffectRealtime(GameObject instance)
    {
        ParticleSystem particle = instance != null ? instance.GetComponentInChildren<ParticleSystem>() : null;

        float duration = 1f;

        if (particle != null)
        {
            ParticleSystem.MainModule main = particle.main;
            duration = main.duration + main.startLifetime.constantMax;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, duration));

        if (instance != null)
        {
            Destroy(instance);
        }
    }

    void CleanupReferences()
    {
        active = false;

        currentTarget = null;
        currentTargetView = null;

        sequence.Clear();
        resolvedCores.Clear();
        resolvedCoreCount = 0;

        currentInputIndex = 0;
    }

    void AbortWhiteChainImmediate()
    {
        if (!active && (roadController == null || !roadController.IsWhiteChainActive))
        {
            return;
        }

        active = false;
        qteActionMap?.Disable();
        SoundManager.Instance?.StopLoop();
        currentTargetView?.FinishFailure();
        HideShadowImmediate();
        roadController?.EndWhiteChainMode();
        celine?.EndWhiteChainCast(true);
        CleanupReferences();

        // 마지막 안전장치.
        Time.timeScale = 1f;
    }

    static bool TryConvertAction(string actionName, out WhiteChainKey key)
    {
        string value = actionName.Replace("QTE_", "").ToUpperInvariant();

        switch (value)
        {
            case "Q": key = WhiteChainKey.Q; return true;
            case "W": key = WhiteChainKey.W; return true;
            case "E": key = WhiteChainKey.E; return true;
            case "A": key = WhiteChainKey.A; return true;
            case "S": key = WhiteChainKey.S; return true;
            case "D": key = WhiteChainKey.D; return true;
            case "Z": key = WhiteChainKey.Z; return true;
            case "X": key = WhiteChainKey.X; return true;
            case "C": key = WhiteChainKey.C; return true;
            case "1": key = WhiteChainKey.One; return true;
            case "2": key = WhiteChainKey.Two; return true;
            case "3": key = WhiteChainKey.Three; return true;
        }
        key = default;
        return false;
    }

    void OnDestroy()
    {
        if (qteActionMap != null)
        {
            foreach (InputAction action in qteActionMap.actions)
            {
                action.performed -= OnQtePerformed;
            }
        }
    }
}