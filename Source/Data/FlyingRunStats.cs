using System;
using UnityEngine;

[Serializable]
public class FlyingRunStats
{
    public int hitCount;
    public float totalDamageTaken;

    public int whiteChainAttempts;
    public int whiteChainSuccesses;
    public int whiteChainFailures;

    public float fastestWhiteChainTime;
    public float totalWhiteChainClearTime;

    public float longestPowerFlyDuration;

    public bool NoHit => hitCount == 0;

    public void Reset()
    {
        hitCount = 0;
        totalDamageTaken = 0f;

        whiteChainAttempts = 0;
        whiteChainSuccesses = 0;
        whiteChainFailures = 0;

        fastestWhiteChainTime = float.PositiveInfinity;

        totalWhiteChainClearTime = 0f;
        longestPowerFlyDuration = 0f;
    }

    public void RegisterHit(float damage)
    {
        hitCount++;
        totalDamageTaken += Mathf.Max(0f, damage);
    }

    public void RegisterWhiteChainAttempt()
    {
        whiteChainAttempts++;
    }

    public void RegisterWhiteChainSuccess(float clearTime, float powerFlyDuration)
    {
        whiteChainSuccesses++;

        totalWhiteChainClearTime += clearTime;
        fastestWhiteChainTime = Mathf.Min(fastestWhiteChainTime, clearTime);
        longestPowerFlyDuration = Mathf.Max(longestPowerFlyDuration, powerFlyDuration);
    }

    public void RegisterWhiteChainFailure()
    {
        whiteChainFailures++;
    }
}