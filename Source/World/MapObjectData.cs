using System.Collections;
using UnityEngine;

public enum MapObjectKind
{
    Collectible,
    PowerUp,
    Obstacle,
    QTEObstacle
}

public enum PowerFlyInteraction
{
    Normal, Ignore, Break
}

public enum ObstacleImpactSize
{
    Small, Large
}

[CreateAssetMenu(fileName = "Cernia Map Object", menuName = "Flying Cernia/Map Object Data")]
public class MapObjectData : ScriptableObject
{
    [Header("기본 정보")]
    public string objectId;
    public string displayName;

    public MapObjectKind kind = MapObjectKind.Collectible;

    [Header("오브젝트 보상")]
    public int scoreBonus;
    public int flyingCoinAmount;
    public float magicRecovery;
    public float hpRecovery;

    [Header("획득 이펙트")]
    public GameObject pickupEffectPrefab;
    public Vector3 pickupEffectOffset;
    public float pickupEffectScale = 1f;

    [Header("연속 획득 콤보")]
    // 획득 시 연속 획득 수에 포함되는가
    public bool countsForCombo = true;

    // 놓쳤을 때 현재 콤보를 초기화하는가
    public bool resetsComboWhenMissed = true;

    [Header("피해")]
    public float damage;

    [Header("자석 능력")]
    public float magnetDuration;

    [Header("파워 플라이")]
    public float powerFlyDuration;

    [Header("자석에 끌리는가")]
    public bool magnetAttractable = true;
    public float magnetPullSpeed = 12f;

    [Header("접촉 규칙")]
    public bool consumeOnContact = true;
    public float contactCooldown = 0.5f;

    [Header("장애물 피격")]
    [Tooltip("Small은 피해와 무적만, Large는 피해와 피격 경직 적용")]
    public ObstacleImpactSize obstacleImpactSize = ObstacleImpactSize.Small;

    // 일반 충돌 후 장애물 제거?
    public bool removeObstacleNormalHit;

    [Header("화이트 체인")]
    public bool requiresWhiteChain;

    [Header("파워 플라이 접촉")]
    public PowerFlyInteraction powerFlyInteraction = PowerFlyInteraction.Normal;
    public int powerFlyBreakSoundIndex = -1;

    [Header("효과음")]
    public int soundEffectIndex = -1;

    void OnValidate()
    {
        scoreBonus = Mathf.Max(0, scoreBonus);
        flyingCoinAmount = Mathf.Max(0, flyingCoinAmount);

        magicRecovery = Mathf.Max(0f, magicRecovery);
        hpRecovery = Mathf.Max(0f, hpRecovery);
        damage = Mathf.Max(0f, damage);

        magnetDuration = Mathf.Max(0f, magnetDuration);
        powerFlyDuration = Mathf.Max(0f, powerFlyDuration);
        magnetPullSpeed = Mathf.Max(0.1f, magnetPullSpeed);
        contactCooldown = Mathf.Max(0f, contactCooldown);

        pickupEffectScale = Mathf.Max(0.01f, pickupEffectScale);
    }
}