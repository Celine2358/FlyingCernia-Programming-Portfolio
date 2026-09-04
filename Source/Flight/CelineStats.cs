using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CelineStats
{
    [Header("체력")]
    public float maxHP = 250f;
    public float hpDrainPerSecond = 2f;

    [Header("마력")]
    public float maxMagic = 100f;
    public float baseWhiteChainMagicCost = 60f;

    [Header("성장 보너스")]
    public float scoreGainMultiplier = 1f;
    public float healingMultiplier = 1f;
    public float magicRecoveryMultiplier = 1f;
    public float damageTakenMultiplier = 1f;
    public float magnetDurationMultiplier = 1f;

    [Header("비행 속도 성장")]
    [Min(0.1f)]
    public float flightSpeedMultiplier = 1f;

    [Tooltip("속도 배율이 1을 초과한 부분 중 몇 비율을 거리 점수 보너스로 줄지")]
    public float speedScoreBonusRate = 0.23f;

    // 현재 HP와 현재 마력은 한 번의 비행에서만 사용하는 런타임 값이다.
    [NonSerialized]
    public float currentHP;

    [NonSerialized]
    public float currentMagic;

    public float NormalizedHP => maxHP <= 0f ? 0f : Mathf.Clamp01(currentHP / maxHP);
    public float NormalizedMagic => maxMagic <= 0f ? 0f : Mathf.Clamp01(currentMagic / maxMagic);

    public float SpeedDistanceScoreMultiplier
    {
        get
        {
            float extraSpeed = Mathf.Max(0f, flightSpeedMultiplier - 1f);
            return 1f + extraSpeed * speedScoreBonusRate;
        }
    }

    public int CalculateDistanceScore(int baseScore)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseScore) * SpeedDistanceScoreMultiplier));
    }

    public float CalculateSpeedScoreMultiplier(float runtimeSpeedMultiplier)
    {
        float extraSpeed = Mathf.Max(0f, runtimeSpeedMultiplier - 1f);

        return 1f + extraSpeed * speedScoreBonusRate;
    }

    /// <summary>
    /// 새 비행을 시작할 때 현재 자원을 초기화한다.
    /// HP는 최대, 마력은 0에서 시작한다.
    /// </summary>
    public void ResetForRun()
    {
        maxHP = Mathf.Max(1f, maxHP);
        maxMagic = Mathf.Max(1f, maxMagic);

        hpDrainPerSecond = Mathf.Max(0f, hpDrainPerSecond);
        baseWhiteChainMagicCost = Mathf.Max(0f, baseWhiteChainMagicCost);

        NormalizeMultipliers();
        currentHP = maxHP;
        currentMagic = 0f;
    }

    public void DrainHP(float deltaTime)
    {
        float drainAmount = hpDrainPerSecond * Mathf.Max(0f, deltaTime);
        currentHP = Mathf.Clamp(currentHP - drainAmount, 0f, maxHP);
    }

    public void Damage(float amount)
    {
        currentHP = Mathf.Clamp(currentHP - Mathf.Max(0f, amount), 0f, maxHP);
    }

    public void Heal(float amount)
    {
        currentHP = Mathf.Clamp(currentHP + Mathf.Max(0f, amount), 0f, maxHP);
    }

    public void AddMagic(float amount)
    {
        currentMagic = Mathf.Clamp(currentMagic + Mathf.Max(0f, amount), 0f, maxMagic);
    }

    public bool TryConsumeMagic(float amount)
    {
        float safeAmount = Mathf.Max(0f, amount);

        if (currentMagic < safeAmount)
        {
            return false;
        }

        currentMagic -= safeAmount;
        return true;
    }

    // 파워 플라이 마력 소모
    public void DrainMagic(float amount)
    {
        currentMagic = Mathf.Clamp(currentMagic - Mathf.Max(0f, amount), 0f, maxMagic);
    }

    public int CalculateScoreGain(int baseScore)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseScore) * scoreGainMultiplier));
    }

    public float CalculateHealing(float baseHealing)
    {
        return Mathf.Max(0f, baseHealing) * healingMultiplier;
    }

    public float CalculateMagicRecovery(float baseRecovery)
    {
        return Mathf.Max(0f, baseRecovery) * magicRecoveryMultiplier;
    }

    public float CalculateDamageTaken(float baseDamage)
    {
        return Mathf.Max(0f, baseDamage) * damageTakenMultiplier;
    }

    public float CalculateMagnetDuration(float baseDuration)
    {
        return Mathf.Max(0f, baseDuration) * magnetDurationMultiplier;
    }

    void NormalizeMultipliers()
    {
        scoreGainMultiplier = Mathf.Max(0f, scoreGainMultiplier);
        healingMultiplier = Mathf.Max(0f, healingMultiplier);
        magicRecoveryMultiplier = Mathf.Max(0f, magicRecoveryMultiplier);
        damageTakenMultiplier = Mathf.Max(0f, damageTakenMultiplier);
        magnetDurationMultiplier = Mathf.Max(0f, magnetDurationMultiplier);
        flightSpeedMultiplier = Mathf.Max(0.1f, flightSpeedMultiplier);
        speedScoreBonusRate = Mathf.Max(0f, speedScoreBonusRate);
    }
}