using System;
using UnityEngine;

public enum FlyingDifficulty
{
    Easy,
    Normal,
    Hard,
    Extreme, // 추후 Hard 윗 난이도
    JetStream // 추후 극악 난이도
}

public enum FlightRank
{
    F = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4,
    S = 5,
    SS = 6 // 추후 최상위 랭크
}

/// <summary>
/// F는 D 미만.
/// D~S의 최소 점수만 저장한다.
/// </summary>
[Serializable]
public class FlightRankCutoffs
{
    public int dScore = 10000;
    public int cScore = 30000;
    public int bScore = 55000;
    public int aScore = 75000;
    public int sScore = 100000;

    public FlightRank Evaluate(int score)
    {
        if (score >= sScore) return FlightRank.S;
        if (score >= aScore) return FlightRank.A;
        if (score >= bScore) return FlightRank.B;
        if (score >= cScore) return FlightRank.C;
        if (score >= dScore) return FlightRank.D;

        return FlightRank.F;
    }
}

/// <summary>
/// 코스 안의 CerniaFlyingMap 한 구간.
/// 
/// totalDistanceMeters는 Selector 표시용.
/// 실제 플레이 거리 계산은 기존 MapSequenceController가 담당한다.
/// </summary>
[Serializable]
public class FlightCourseSegment
{
    public CerniaFlyingMap map;
    [Tooltip("이 구간의 설계 거리. Map Select UI 표시용.")]
    public float totalDistanceMeters;
}

/// <summary>
/// 난이도 해금 규칙.
/// Clear 또는 Rank 중 하나만 만족해도 해금
/// </summary>
[Serializable]
public class FlightDifficultyUnlockRule
{
    // 기본 해금
    public bool unlockByDefault = true;

    [Header("선행 난이도")]
    public FlyingDifficulty prerequisiteDifficulty = FlyingDifficulty.Normal; // 이 난이도가 전제 조건

    [Header("해금 조건")]
    public bool unlockOnClear = true;
    public bool unlockOnRank = false;
    public FlightRank minimumRank = FlightRank.B;
}

[Serializable]
public class FlightCourseUnlockRule
{
    [Header("기본 해금")]
    public bool unlockByDefault = true;

    [Header("선행 Copurse")]
    public CerniaFlightCourse prerequisiteCourse;

    [Header("선행 난이도")]
    public FlyingDifficulty prerequisiteDifficulty = FlyingDifficulty.Normal;

    [Header("해금 조건")]
    public bool unlockOnClear = true;
    public bool unlockOnRank = false;
    public FlightRank minimumRank = FlightRank.B;
}

/// <summary>
/// 하나의 난이도.
/// 예: 세르니아 구름길 Easy.
/// </summary>
[Serializable]
public class CerniaFlightCourseDifficulty
{
    [Header("고유 ID")]
    public string difficultyId = "easy";
    public FlyingDifficulty difficulty = FlyingDifficulty.Easy;

    [Header("진입 씬")]
    public string sceneName = "CerniaCloudRoad";

    [Header("맵 선택 UI")]
    public Sprite previewImage;

    [Header("튜토리얼")]
    public bool marksTutorialClear = false;

    [Header("권장 레벨")]
    public int recommendedLevel = 0;

    [Header("비행 구간")]
    public FlightCourseSegment[] segments;

    [Header("Rank")]
    public FlightRankCutoffs rankCutoffs = new FlightRankCutoffs();

    [Header("해금")]
    public FlightDifficultyUnlockRule unlock = new FlightDifficultyUnlockRule();

    /// <summary>
    /// 이 난이도의 전체 비행거리.
    /// </summary>
    public float TotalDistanceMeters
    {
        get
        {
            if (segments == null)
            {
                return 0f;
            }

            float total = 0f;

            foreach (FlightCourseSegment segment in segments)
            {
                if (segment == null)
                {
                    continue;
                }

                total += Mathf.Max(0f, segment.totalDistanceMeters);
            }

            return total;
        }
    }

    /// <summary>
    /// MapSequenceController에서 사용할
    /// CerniaFlyingMap 배열을 만든다.
    /// </summary>
    public CerniaFlyingMap[] GetMapSequence()
    {
        if (segments == null)
        {
            return Array.Empty<CerniaFlyingMap>();
        }

        CerniaFlyingMap[] maps = new CerniaFlyingMap[segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            maps[i] = segments[i] != null ? segments[i].map : null;
        }

        return maps;
    }

    /// <summary>
    /// 점수 + 진행 상황으로 최종 Rank 결정.
    /// 
    /// 첫 구간도 통과하지 못함 -> D 이하
    /// 중도 실패 -> B 이하
    /// 완주 -> A/S 가능
    /// </summary>
    public FlightRank CalculateRank(int score, bool cleared, int completedSectionCount)
    {
        FlightRank rank = rankCutoffs.Evaluate(score);

        if (cleared)
        {
            return rank;
        }

        // 첫 맵조차 통과하지 못했다.
        if (completedSectionCount <= 0)
        {
            return MinRank(rank, FlightRank.D);
        }

        // 한 구간 이상 통과했지만 완주는 실패.
        // A / S는 완주 전용.
        return MinRank(rank, FlightRank.B);
    }

    // 랭크를 int로 변환시켜 최소 랭크 구하기
    static FlightRank MinRank(FlightRank a, FlightRank b)
    {
        return (FlightRank)Mathf.Min((int)a, (int)b);
    }

    public string DisplayDifficultyName => difficulty.ToString();
}

/// <summary>
/// 세르니아 구름길, 바위산 섬록 등
/// 하나의 큰 비행 지역 데이터.
/// </summary>
[CreateAssetMenu(fileName = "CerniaFlightCourse", menuName = "Flying Cernia/Flight Course")]
public class CerniaFlightCourse : ScriptableObject
{
    [Header("고유 ID")]
    public string courseId = "cernia_cloud_road";

    [Header("플레이어 표시 이름")]
    public string displayName = "세르니아 구름길";

    [Header("난이도")]
    public CerniaFlightCourseDifficulty[] difficulties;

    [Header("Course 해금")]
    public FlightCourseUnlockRule unlock = new FlightCourseUnlockRule();

    public CerniaFlightCourseDifficulty GetDifficulty(int index)
    {
        if (difficulties == null || difficulties.Length == 0)
        {
            return null;
        }

        int safeIndex = Mathf.Clamp(index, 0, difficulties.Length - 1);

        return difficulties[safeIndex];
    }

    public int FindDifficultyIndex(FlyingDifficulty difficulty)
    {
        if (difficulties == null)
        {
            return -1;
        }

        for (int i = 0; i < difficulties.Length; i++)
        {
            if (difficulties[i] != null && difficulties[i].difficulty == difficulty)
            {
                return i;
            }
        }

        return -1;
    }
}