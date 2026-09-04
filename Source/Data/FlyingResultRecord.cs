using System;
using UnityEngine;

/// <summary>
/// 한 번 완료한 Flying Cernia 비행의 최종 결과
/// 
/// 런타임 상태가 아닌, Result가 확정된 시점의 Snapshot이다.
/// </summary>
[Serializable]
public class FlyingResultRecord
{
    public string flyingMapName;

    /// <summary>
    /// 최종 EndPoint까지 정상적으로 비행을 완료했는가?
    /// </summary>
    public bool cleared;

    // 기본 결과
    public int baseFlyingPoint;
    public float flyingDistanceMeters;
    public int flyingCoin;

    // 피격 기록
    public int hitCount;
    public float totalDamageTaken;

    // White Chain
    public int whiteChainAttempts;
    public int whiteChainSuccesses;
    public int whiteChainFailures;

    // White Chain 성공 기록이 없으면 -1.
    public float fastestWhiteChainTime = -1f;
    public float totalWhiteChainClearTime;

    public float longestPowerFlyDuration;

    // Result 계산
    public float clearBonusRate;
    public float noHitBonusRate;
    public float whiteChainBonusRate;
    public float totalBonusRate;

    // 최종 점수!
    public int finalScore;

    // Course
    public string courseId;
    public string difficultyId;

    // 진행
    public int completedSectionCount;
    public int totalSectionCount;

    // 죵료 시 자원
    public float remainingHP;
    public float remainingMagic;

    // 이 비행에서의 랭크
    public FlightRank rank;

    // UTC Unix Time
    public long completedAtUnix;
}