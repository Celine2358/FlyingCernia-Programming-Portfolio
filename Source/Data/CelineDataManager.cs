using System.Collections;
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Flying Cernia의 로컬 데이터를 관리하는 매니저
/// 닉네임, 프로필ID, 최고 점수 등 같은 플레이 데이터와 옵션 데이터를 JSON으로 저장한다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public partial class CelineDataManager : MonoBehaviour
{
    public static CelineDataManager Instance { get; private set; }

    private const string DataDirectoryName = "CelineData";
    private const string PlayerFileName = "player.fcdata";
    private const string OptionsFileName = "options.json";

    // 클라이언트 안에 들어가는 키는 완전한 보안 수단이 아니다.
    // 알파~베타 단계에서 casual edit 방지용으로만 사용한다.
    private const string ObfuscationKey = "FlyingCernia_";

    [Header("Profile Protection")]
    [SerializeField] private bool useProfileHmac = true;
    [SerializeField] private bool useProfileXorObfuscation = true;

    [Header("Debug")]
    [SerializeField] private bool alsoClearPlayerPrefsWhenDeleting = true;

    public CelinePlayerData PlayerData { get; private set; }
    public CelineOptionsData OptionsData { get; private set; }

    public bool HasNickname =>
        PlayerData != null &&
        PlayerData.hasNickname &&
        !string.IsNullOrWhiteSpace(PlayerData.nickname);

    private string DataFolderPath => Path.Combine(Application.persistentDataPath, DataDirectoryName);
    private string PlayerFilePath => Path.Combine(DataFolderPath, PlayerFileName);
    private string OptionsFilePath => Path.Combine(DataFolderPath, OptionsFileName);

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAll();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 모든 저장 데이터를 불러온다.
    /// 파일이 없거나 손상되어 있으면 기본값으로 시작한다.
    /// </summary>
    public void LoadAll()
    {
        Directory.CreateDirectory(DataFolderPath);

        PlayerData = LoadPlayerData();
        OptionsData = LoadOptionsData();

        EnsurePlayerDefaults();

        OptionsData.Normalize();

        // 게임 시작 즉시 해상도, FPS, 옵션값을 반영한다.
        CelineOptionsRuntime.ApplyAll(OptionsData);
    }

    /// <summary>
    /// 닉네임을 저장한다.
    /// 닉네임 검증은 NicknameDecider에서 먼저 처리한다.
    /// </summary>
    public void SetNickname(string nickname)
    {
        if (PlayerData == null) PlayerData = CreateDefaultPlayerData();

        PlayerData.nickname = nickname.Trim();
        PlayerData.hasNickname = true;
        PlayerData.updatedAtUnix = GetUnixNow();

        SavePlayerData();
    }

    /// <summary>
    /// 한 번 완료한 비행 결과를 플레이어 데이터에 기록한다.
    /// 최고 점수, 최고 거리, 누적 코인도 함께 갱신한다.
    /// </summary>
    public bool RecordFlyingResult(FlyingResultRecord result, bool marksTutorialClear = false)
    {
        if (result == null) return false;

        if (PlayerData == null) PlayerData = CreateDefaultPlayerData();

        // 가장 최근 비행 결과
        PlayerData.lastFlightResult = result;

        // 점수는 중도 종료여도 기록
        PlayerData.bestScore = Mathf.Max(PlayerData.bestScore, result.finalScore);

        if (result.cleared)
        {
            PlayerData.clearCount++;
            if (marksTutorialClear)
            {
                PlayerData.hasClearedTutorial = true;
            }
            // 최고 완주 거리.
            PlayerData.bestDistance = Mathf.Max(PlayerData.bestDistance, result.flyingDistanceMeters);
            // 코인은 완주한 경우에만 실제 정산
            PlayerData.totalFlyingCoin = Mathf.Max(0, PlayerData.totalFlyingCoin) + Mathf.Max(0, result.flyingCoin);
        }

        bool newBestRecord = false;

        if (!string.IsNullOrWhiteSpace(result.courseId) && !string.IsNullOrWhiteSpace(result.difficultyId))
        {
            FlyingCourseRecord courseRecord = GetOrCreateCourseRecord(result.courseId, result.difficultyId);
            courseRecord.playCount++;

            if (result.cleared)
            {
                courseRecord.clearCount++;

                // MapSequenceController가 실제로 계산한
                // 완주 전체 거리를 기억한다.
                if (result.flyingDistanceMeters > 0f)
                {
                    courseRecord.clearedCourseDistanceMeters = result.flyingDistanceMeters;
                }
            }

            if ((int)result.rank > (int)courseRecord.bestRank)
            {
                courseRecord.bestRank = result.rank;
            }

            FlyingResultRecord previousBest = courseRecord.bestScoreResult;

            // 최초 기록도 첫 최고기록으로 취급.
            newBestRecord = previousBest == null || result.finalScore > previousBest.finalScore ||
                (
                    result.finalScore == previousBest.finalScore && (int)result.rank > (int)previousBest.rank
                );

            if (courseRecord.topResults == null)
            {
                courseRecord.topResults = new List<FlyingResultRecord>();
            }

            // 이번 Run을 후보 기록에 추가.
            courseRecord.topResults.Add(result);

            SortAndTrimTopResults(courseRecord);
        }

        SavePlayerData();
        return newBestRecord;
    }

    public FlyingCourseRecord GetCourseRecord(string courseId, string difficultyId)
    {
        if (PlayerData == null || PlayerData.courseRecords == null)
        {
            return null;
        }

        return PlayerData.courseRecords.Find(
            x =>
                x != null &&
                x.courseId == courseId &&
                x.difficultyId == difficultyId);
    }

    FlyingCourseRecord GetOrCreateCourseRecord(string courseId, string difficultyId)
    {
        if (PlayerData.courseRecords == null)
        {
            PlayerData.courseRecords = new List<FlyingCourseRecord>();
        }

        FlyingCourseRecord record = GetCourseRecord(courseId, difficultyId);

        if (record != null)
        {
            return record;
        }

        record =
            new FlyingCourseRecord
            {
                courseId = courseId,
                difficultyId = difficultyId
            };

        PlayerData.courseRecords.Add(record);

        return record;
    }

    /// <summary>
    /// 점수 내림차순으로 정렬하고
    /// 최대 5개의 기록만 남긴다.
    /// </summary>
    static void SortAndTrimTopResults(FlyingCourseRecord courseRecord)
    {
        if (courseRecord == null)
        {
            return;
        }

        if (courseRecord.topResults == null)
        {
            courseRecord.topResults = new List<FlyingResultRecord>();
        }

        courseRecord.topResults.RemoveAll(x => x == null);

        courseRecord.topResults.Sort((a, b) =>
            {
                // 최종 점수가 높은 기록 우선
                int compare = b.finalScore.CompareTo(a.finalScore);

                if (compare != 0)
                {
                    return compare;
                }

                // 동점이면 Rank가 높은 기록.
                compare = ((int)b.rank).CompareTo((int)a.rank);

                if (compare != 0)
                {
                    return compare;
                }

                // 완주 기록 우선.
                compare = b.cleared.CompareTo(a.cleared);

                if (compare != 0)
                {
                    return compare;
                }

                // 완전히 같다면 최근 기록 우선
                return b.completedAtUnix.CompareTo(a.completedAtUnix);
            });

        if (courseRecord.topResults.Count > 5)
        {
            courseRecord.topResults.RemoveRange(5, courseRecord.topResults.Count - 5);
        }

        // 기존 시스템과의 호환성을 위해
        // 1등을 항상 bestScoreResult로 유지한다.
        courseRecord.bestScoreResult = courseRecord.topResults.Count > 0 ? courseRecord.topResults[0] : null;
    }

    public void SaveAll()
    {
        SavePlayerData();
        SaveOptionsData();
    }

    public void SavePlayerData()
    {
        Directory.CreateDirectory(DataFolderPath);
        if (PlayerData == null) PlayerData = CreateDefaultPlayerData();

        PlayerData.updatedAtUnix = GetUnixNow();

        string json = JsonUtility.ToJson(PlayerData, true);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);

        if (useProfileXorObfuscation) payloadBytes = XorBytes(payloadBytes, LocalSecretKey);

        string payloadBase64 = Convert.ToBase64String(payloadBytes);

        CelineSaveEnvelope envelope = new CelineSaveEnvelope
        {
            version = 1,
            payloadBase64 = payloadBase64,
            hmacSha256 = useProfileHmac ? CreateHmac(payloadBase64) : string.Empty
        };

        string envelopeJson = JsonUtility.ToJson(envelope, true);
        File.WriteAllText(PlayerFilePath, envelopeJson, Encoding.UTF8);
    }

    public void SaveOptionsData()
    {
        Directory.CreateDirectory(DataFolderPath);

        if (OptionsData == null)
        {
            OptionsData = new CelineOptionsData();
        }
        OptionsData.Normalize();

        string json = JsonUtility.ToJson(OptionsData, true);
        File.WriteAllText(OptionsFilePath, json, Encoding.UTF8);
    }

    CelinePlayerData LoadPlayerData()
    {
        if (!File.Exists(PlayerFilePath)) return CreateDefaultPlayerData();

        try
        {
            string envelopeJson = File.ReadAllText(PlayerFilePath, Encoding.UTF8);
            CelineSaveEnvelope envelope = JsonUtility.FromJson<CelineSaveEnvelope>(envelopeJson);

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.payloadBase64))
            {
                Debug.LogWarning("플레이어 데이터 형식이 올바르지 않아 기본값으로 시작합니다.");
                return CreateDefaultPlayerData();
            }

            if (useProfileHmac)
            {
                string expectedHmac = CreateHmac(envelope.payloadBase64);

                if (!FixedEqualsBase64(expectedHmac, envelope.hmacSha256))
                {
                    Debug.LogWarning("플레이어 데이터 검증에 실패했습니다. 데이터가 수정되었을 수 있습니다.");
                    return CreateDefaultPlayerData();
                }
            }

            byte[] payloadBytes = Convert.FromBase64String(envelope.payloadBase64);

            if (useProfileXorObfuscation) payloadBytes = XorBytes(payloadBytes, LocalSecretKey);

            string json = Encoding.UTF8.GetString(payloadBytes);
            CelinePlayerData data = JsonUtility.FromJson<CelinePlayerData>(json);

            return data ?? CreateDefaultPlayerData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"플레이어 데이터 로드 실패: {ex.Message}");
            return CreateDefaultPlayerData();
        }
    }

    private CelineOptionsData LoadOptionsData()
    {
        CelineOptionsData data = new CelineOptionsData();

        if (!File.Exists(OptionsFilePath))
        {
            data.Normalize();
            return data;
        }

        try
        {
            string json = File.ReadAllText(OptionsFilePath, Encoding.UTF8);

            // 기본값이 들어간 객체 위에 JSON 값만 덮어쓴다.
            // 새 필드가 JSON에 없으면 기본값이 그대로 유지된다.
            JsonUtility.FromJsonOverwrite(json, data);
            data.Normalize();
            return data;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"옵션 데이터 로드 실패: {ex.Message}");

            data = new CelineOptionsData();
            data.Normalize();

            return data;
        }
    }

    /// <summary>
    /// 옵션 UI에서 확정한 Draft를 실제 데이터로 복사하고
    /// JSON 저장 및 런타임 적용까지 수행한다.
    /// </summary>
    public void CommitOptions(CelineOptionsData newOptions)
    {
        if (newOptions == null)
        {
            Debug.LogWarning("저장할 옵션 데이터가 없습니다.");
            return;
        }

        if (OptionsData == null)
        {
            OptionsData = new CelineOptionsData();
        }

        OptionsData.CopyFrom(newOptions);
        SaveOptionsData();

        CelineOptionsRuntime.ApplyAll(OptionsData);
    }

    void EnsurePlayerDefaults()
    {
        if (PlayerData == null) PlayerData = CreateDefaultPlayerData();

        long now = GetUnixNow();

        if (string.IsNullOrWhiteSpace(PlayerData.profileId)) PlayerData.profileId = Guid.NewGuid().ToString("N");
        if (PlayerData.createdAtUnix <= 0) PlayerData.createdAtUnix = now;
        if (PlayerData.updatedAtUnix <= 0) PlayerData.updatedAtUnix = now;
        if (PlayerData.lastFlightResult == null) PlayerData.lastFlightResult = new FlyingResultRecord();
        if (PlayerData.courseRecords == null) PlayerData.courseRecords = new List<FlyingCourseRecord>();

        // 기존 M5-B 저장 데이터를
        // Top 5 시스템으로 자동 마이그레이션.
        foreach (FlyingCourseRecord record in PlayerData.courseRecords)
        {
            if (record == null)
            {
                continue;
            }

            if (record.topResults == null)
            {
                record.topResults = new List<FlyingResultRecord>();
            }

            // 기존 최고기록 하나가 존재한다면
            // Top Results의 첫 기록으로 옮긴다.
            if (record.topResults.Count == 0 && record.bestScoreResult != null)
            {
                record.topResults.Add(record.bestScoreResult);
            }

            SortAndTrimTopResults(record);
        }

        PlayerData.dataVersion = 2;
    }

    static CelinePlayerData CreateDefaultPlayerData()
    {
        long now = GetUnixNow();

        return new CelinePlayerData
        {
            dataVersion = 2,
            profileId = Guid.NewGuid().ToString("N"),
            nickname = string.Empty,
            hasNickname = false,
            createdAtUnix = now,
            updatedAtUnix = now,
            bestScore = 0,
            bestDistance = 0f,
            totalFlyingCoin = 0,
            hasClearedTutorial = false,
            clearCount = 0,
            lastFlightResult = new FlyingResultRecord()
        };
    }

    static long GetUnixNow()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    static string CreateHmac(string payloadBase64)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(LocalSecretKey);
        byte[] dataBytes = Encoding.UTF8.GetBytes(payloadBase64);

        using (HMACSHA256 hmac = new HMACSHA256(keyBytes))
        {
            byte[] hash = hmac.ComputeHash(dataBytes);
            return Convert.ToBase64String(hash);
        }
    }

    static byte[] XorBytes(byte[] source, string key)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] result = new byte[source.Length];

        for (int i = 0; i < source.Length; i++) result[i] = (byte)(source[i] ^ keyBytes[i % keyBytes.Length]);

        return result;
    }

    static bool FixedEqualsBase64(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        byte[] aa;
        byte[] bb;

        try
        {
            aa = Convert.FromBase64String(a);
            bb = Convert.FromBase64String(b);
        }
        catch
        {
            return false;
        }

        if (aa.Length != bb.Length) return false;
        int diff = 0;

        for (int i = 0; i < aa.Length; i++) diff |= aa[i] ^ bb[i];
        return diff == 0;
    }

    [ContextMenu("Debug/Delete All Celine Data")]
    public void DeleteAllCelineData()
    {
        try
        {
            if (Directory.Exists(DataFolderPath)) Directory.Delete(DataFolderPath, true);

            if (alsoClearPlayerPrefsWhenDeleting)
            {
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
            }

            PlayerData = CreateDefaultPlayerData();
            OptionsData = new CelineOptionsData();

            OptionsData.Normalize();
            CelineOptionsRuntime.ApplyAll(OptionsData);

            Debug.Log("Flying Cernia 로컬 데이터를 모두 삭제했습니다.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"데이터 삭제 실패: {ex.Message}");
        }
    }

    [ContextMenu("Debug/Print Data Folder Path")]
    public void PrintDataFolderPath()
    {
        Debug.Log($"Flying Cernia Data Folder: {DataFolderPath}");
    }
}

[Serializable]
public sealed class CelineSaveEnvelope
{
    public int version;
    public string payloadBase64;
    public string hmacSha256;
}

[Serializable]
public sealed class CelinePlayerData
{
    public int dataVersion;
    public string profileId;

    public string nickname;
    public bool hasNickname;

    public long createdAtUnix;
    public long updatedAtUnix;

    public int bestScore;
    public float bestDistance;
    public int totalFlyingCoin;

    // Flying Cernia 진행도
    public bool hasClearedTutorial;

    // Flying Cernia 비행 결과
    public int clearCount;
    public FlyingResultRecord lastFlightResult;

    public List<FlyingCourseRecord> courseRecords = new List<FlyingCourseRecord>();
}

[Serializable]
public class FlyingCourseRecord
{
    public string courseId;
    public string difficultyId;
    public int playCount;
    public int clearCount;

    // 지금까지 달성한 가장 높은 랭크
    public FlightRank bestRank = FlightRank.F;

    // 최고 점수 Run 전체 스냅샷
    public FlyingResultRecord bestScoreResult;

    // 최고 점수부터 최대 5개까지 보관
    public List<FlyingResultRecord> topResults = new List<FlyingResultRecord>();

    // 이 Course/Difficulty를 완주했을 때의
    // 실제 계산된 전체 비행 거리.
    public float clearedCourseDistanceMeters;
}