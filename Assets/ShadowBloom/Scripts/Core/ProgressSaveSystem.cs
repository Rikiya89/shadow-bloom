using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowBloom
{
    public interface IProgressStore
    {
        string Load();
        void Save(string json);
    }

    public sealed class MemoryProgressStore : IProgressStore
    {
        public string Json;
        public string Load() => Json;
        public void Save(string json) => Json = json;
    }

    [Serializable]
    public sealed class ProgressData
    {
        public int version = 3;
        public List<string> completedStageIds = new List<string>();
        public int lastStageIndex;
        public int highestUnlockedStageIndex;
        public bool soundMuted;
        // v3: Daily Shadow. Day numbers from DailyShadow.DayNumber; 0 = never.
        public int dailyStreak;
        public int bestStreak;
        public int lastDailyDay;
        public List<int> gardenDays = new List<int>();
    }

    /// <summary>Local progress keyed by stable stage ids, so reordering or adding stages keeps old saves valid.</summary>
    public sealed class ProgressSaveSystem
    {
        readonly IProgressStore _store;
        public ProgressData Data { get; private set; }

        public ProgressSaveSystem(IProgressStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Data = Parse(_store.Load());
        }

        public bool IsCompleted(string stageId) => Data.completedStageIds.Contains(stageId);

        public void MarkCompleted(string stageId, int stageIndex)
        {
            if (string.IsNullOrEmpty(stageId) || stageIndex < 0) return;
            if (!Data.completedStageIds.Contains(stageId)) Data.completedStageIds.Add(stageId);
            Data.lastStageIndex = Math.Max(Data.lastStageIndex, stageIndex);
            Data.highestUnlockedStageIndex = Math.Max(Data.highestUnlockedStageIndex, stageIndex + 1);
            Persist();
        }

        public bool IsCampaignComplete(IReadOnlyList<string> orderedStageIds)
        {
            if (orderedStageIds.Count == 0) return false;
            for (int i = 0; i < orderedStageIds.Count; i++)
                if (!IsCompleted(orderedStageIds[i])) return false;
            return true;
        }

        public bool SolvedToday(int day) => day > 0 && Data.lastDailyDay == day;

        /// <summary>A daily solve: advances the streak once per day and plants the day in the garden (7 newest kept).</summary>
        public void RecordDaily(int day)
        {
            if (day <= 0) return;
            Data.dailyStreak = DailyShadow.NextStreak(Data.dailyStreak, Data.lastDailyDay, day);
            Data.bestStreak = Math.Max(Data.bestStreak, Data.dailyStreak);
            Data.lastDailyDay = day;
            if (!Data.gardenDays.Contains(day)) Data.gardenDays.Add(day);
            while (Data.gardenDays.Count > DailyShadow.GardenCapacity) Data.gardenDays.RemoveAt(0);
            Persist();
        }

        public void SetLastStage(int stageIndex)
        {
            Data.lastStageIndex = Math.Max(0, stageIndex);
            Persist();
        }

        public void SetSoundMuted(bool muted)
        {
            if (Data.soundMuted == muted) return;
            Data.soundMuted = muted;
            Persist();
        }

        /// <summary>Reconcile old saves and clamp the unlock to the current stable-id stage catalogue.</summary>
        public int HighestUnlockedStage(IReadOnlyList<string> orderedStageIds)
        {
            int highest = 0;
            for (int i = 0; i < orderedStageIds.Count; i++)
                if (IsCompleted(orderedStageIds[i])) highest = i + 1;
            Data.highestUnlockedStageIndex = Math.Clamp(highest, 0, Math.Max(0, orderedStageIds.Count - 1));
            Persist();
            return Data.highestUnlockedStageIndex;
        }

        public void ResetAll()
        {
            Data = new ProgressData();
            Persist();
        }

        /// <summary>First stage that is not yet completed (or 0 when all are done).</summary>
        public int FirstIncomplete(IReadOnlyList<string> orderedStageIds)
        {
            for (int i = 0; i < orderedStageIds.Count; i++)
            {
                if (!IsCompleted(orderedStageIds[i])) return i;
            }
            return 0;
        }

        void Persist() => _store.Save(JsonUtility.ToJson(Data));

        static ProgressData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new ProgressData();
            try
            {
                var d = JsonUtility.FromJson<ProgressData>(json);
                if (d == null) return new ProgressData();
                d.completedStageIds ??= new List<string>();
                d.completedStageIds.RemoveAll(string.IsNullOrEmpty);
                d.completedStageIds = new List<string>(new HashSet<string>(d.completedStageIds));
                d.lastStageIndex = Math.Max(0, d.lastStageIndex);
                d.highestUnlockedStageIndex = Math.Max(0, d.highestUnlockedStageIndex);
                d.dailyStreak = Math.Max(0, d.dailyStreak);
                d.bestStreak = Math.Max(d.dailyStreak, d.bestStreak);
                d.lastDailyDay = Math.Max(0, d.lastDailyDay);
                d.gardenDays ??= new List<int>();
                d.gardenDays.RemoveAll(x => x <= 0);
                d.gardenDays.Sort();
                while (d.gardenDays.Count > DailyShadow.GardenCapacity) d.gardenDays.RemoveAt(0);
                d.version = 3;
                return d;
            }
            catch (ArgumentException)
            {
                return new ProgressData(); // corrupt save: start fresh rather than crash
            }
        }
    }
}
