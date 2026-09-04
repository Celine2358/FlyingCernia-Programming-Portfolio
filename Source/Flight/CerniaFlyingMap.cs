using UnityEngine;

/// <summary>
/// 세르니아 비행 맵들의 비행 물리, 거리, 자원 규칙을 보관한다.
/// 맵 난이도 규칙을 저장하는 게임 디자인용 설정 자산.
/// </summary>

[CreateAssetMenu(fileName = "Cernia Flying Map Settings", menuName = "Flying Cernia/Map Settings")]
public class CerniaFlyingMap : ScriptableObject
{
    [Header("맵 정보")]
    public string mapName = "";
    public Sprite mapSprite;

    [Header("맵 오브젝트 패턴")]
    public GameObject mapObjectPatternPrefab;

    [Header("구름 배치")]
    public CerniaCloudSettings cloudFieldSettings;

    [Header("세로 비행 물리")]
    [Min(0f)]
    // 중력 가속도
    public float gravityAcceleration = 18f;

    [Min(0f)]
    // 상승 가속도
    public float riseAcceleration = 30f;

    [Tooltip("현재 세로 속도의 반대 방향으로 작용하는 감쇠 계수")]
    [Min(0f)]
    // vertical 수평의
    public float verticalDrag = 1.6f;

    [Tooltip("상승 입력이 0과 1 사이에서 변하는 속도")]
    [Min(0.01f)]
    public float inputResponse = 7f;

    [Min(0.01f)]
    // 최대 상승 속도
    public float maxRiseSpeed = 7f;

    [Min(0.01f)]
    // 최대 하락 속도
    public float maxFallSpeed = 8f;

    [Header("시각적 기울기")]
    public float riseAngle = 15f;
    public float fallAngle = -22f;

    [Min(0.01f)]
    // 부드럽게 전환/회전
    public float rotationSmoothness = 8f;

    [Header("목표 기본 완주 시간")]
    [Tooltip("파워 플라이와 Pause를 제외한 목표 플레이 시간")]
    public float targetFlightSeconds = 120f;

    [Header("목표 시간 기반 전진")]
    public bool useTargetDurationForForwardSpeed = true;

    [Header("전진과 거리")]
    [Tooltip("바람 방해가 없을 때 초당 증가할 비행 거리")]
    [Min(0.01f)]
    public float metersPerSecond = 10f;

    [Tooltip("Unity 월드 좌표 1 unit을 몇 m로 계산할지")]
    [Min(0.01f)]
    public float metersPerWorldUnit = 10f;

    [Header("거리 1m당 기본 비행 점수")]
    [Min(0)]
    public int pointPerMeter = 10;

    [Header("바람 방해 : 0이면 방해 없음. 0.2면 거리와 월드 스크롤 속도가 20% 감소한다.")]
    [Range(0f, 0.95f)]
    public float windInterference = 0f;

    [Tooltip("활성화하면 바람 방해가 목표 시간보다 실제 완주를 늦춘다.")]
    public bool windAffectsTargetDuration = false;

    [Header("맵 레벨")]
    [Min(1)]
    public int mapLevel = 1;

    [Header("맵 레벨 당 화이트 체인 요구량 증가 비율")]
    // 맵 레벨이 1 증가할 때 화이트 체인 요구량 증가 비율.
    // 0.2면 레벨마다 20% 증가한다.
    [Min(0f)]
    public float whiteChainCostIncreasePerLevel = 0.2f;

    /// <summary>
    /// 바람 방해를 반영한 속도 배율.
    /// </summary>
    public float SpeedMultiplier => 1f - Mathf.Clamp(windInterference, 0f, 0.95f);

    /// <summary>
    /// 바람 방해를 반영한 실제 거리 증가 속도.
    /// </summary>
    public float EffectiveMetersPerSecond => metersPerSecond * SpeedMultiplier;

    /// <summary>
    /// 셀리느 Rigidbody2D에 적용할 실제 X축 속도.
    /// </summary>
    public float EffectiveWorldUnitsPerSecond => EffectiveMetersPerSecond / Mathf.Max(0.01f, metersPerWorldUnit);

    /// <summary>
    /// 맵 레벨을 반영한 화이트 체인 마력 요구량을 계산한다.
    ///
    /// 레벨 1: 기본 비용
    /// 레벨 2: 기본 비용 × (1 + 증가율)
    /// 레벨 3: 기본 비용 × (1 + 증가율 × 2)
    /// </summary>
    public float GetWhiteChainMagicCost(float baseCost)
    {
        int additionalLevel = Mathf.Max(0, mapLevel - 1);
        float multiplier = 1f + whiteChainCostIncreasePerLevel * additionalLevel;

        return Mathf.Max(0f, baseCost * multiplier);
    }

    /// <summary>
    /// 현재 맵 길이와 셀리느 성장 속도를 반영한
    /// 실제 X축 월드 속도를 반환한다.
    /// </summary>
    public float GetForwardWorldSpeed(float segmentWorldDistance, float celineSpeedMultiplier)
    {
        float speed;

        if (useTargetDurationForForwardSpeed)
        {
            float safeDuration = Mathf.Max(0.01f, targetFlightSeconds);

            speed = Mathf.Max(0f, segmentWorldDistance) / safeDuration;

            if (windAffectsTargetDuration)
            {
                speed *= SpeedMultiplier;
            }
        }
        else
        {
            speed = EffectiveWorldUnitsPerSecond;
        }

        return speed * Mathf.Max(0.1f, celineSpeedMultiplier);
    }

    void OnValidate()
    {
        gravityAcceleration = Mathf.Max(0f, gravityAcceleration);
        riseAcceleration = Mathf.Max(0f, riseAcceleration);
        verticalDrag = Mathf.Max(0f, verticalDrag);

        inputResponse = Mathf.Max(0.01f, inputResponse);
        maxRiseSpeed = Mathf.Max(0.01f, maxRiseSpeed);
        maxFallSpeed = Mathf.Max(0.01f, maxFallSpeed);

        rotationSmoothness = Mathf.Max(0.01f, rotationSmoothness);
        metersPerSecond = Mathf.Max(0.01f, metersPerSecond);
        metersPerWorldUnit = Mathf.Max(0.01f, metersPerWorldUnit);

        pointPerMeter = Mathf.Max(0, pointPerMeter);

        windInterference = Mathf.Clamp(windInterference, 0f, 0.95f);
        mapLevel = Mathf.Max(1, mapLevel);
        whiteChainCostIncreasePerLevel = Mathf.Max(0f, whiteChainCostIncreasePerLevel);
    }
}