using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// The SRD Leadership tables, transcribed from `featsList.html` (base) and `epicFeats.html`
/// (Table: Epic Leadership).
///
/// Two tables, not one formula. The base table stops at score 25 — "The Leadership feat provides
/// no benefit for leadership scores beyond 25" — and the Epic Leadership feat replaces it with one
/// that runs to 40 and adds follower levels 7th through 10th. Both give cohort level as a column,
/// which is why it is looked up here rather than derived: the SRD progression is irregular
/// (score 20 → 14th, 21 → 15th, 22 → 15th) and no arithmetic reproduces it.
/// </summary>
public static class LeadershipTables
{
    /// <summary>
    /// Cohort level from the base table. The separate "can't recruit a cohort of his or her level
    /// or higher" cap is applied by the caller, which knows the character's level.
    /// </summary>
    public static int LookupCohortLevel(int leadershipScore) => leadershipScore switch
    {
        <= 1 => 0,
        2 => 1,
        3 => 2,
        4 or 5 => 3,
        6 => 4,
        7 or 8 => 5,
        9 => 6,
        10 or 11 => 7,
        12 => 8,
        13 => 9,
        14 or 15 => 10,
        16 => 11,
        17 or 18 => 12,
        19 => 13,
        20 => 14,
        21 or 22 => 15,
        23 => 16,
        _ => 17,
    };

    /// <summary>Cohort level from Table: Epic Leadership. Below 25 the base table still applies.</summary>
    public static int LookupEpicCohortLevel(int leadershipScore)
    {
        if (leadershipScore < 25) return LookupCohortLevel(leadershipScore);
        if (leadershipScore > 40)
            // "The maximum cohort level increases by 1 for every 2 points of Leadership above 40."
            return 25 + (leadershipScore - 40) / 2;

        return leadershipScore switch
        {
            25 => 17,
            26 or 27 => 18,
            28 or 29 => 19,
            30 or 31 => 20,
            32 or 33 => 21,
            34 or 35 => 22,
            36 or 37 => 23,
            38 or 39 => 24,
            _ => 25,
        };
    }

    /// <summary>
    /// Picks the right cohort-level table for a character: Epic Leadership replaces the base one.
    /// Saves every caller from repeating the feat check.
    /// </summary>
    public static int LookupCohortLevelFor(IEnumerable<string> feats, int leadershipScore) =>
        feats.Contains("feat:epic_leadership")
            ? LookupEpicCohortLevel(leadershipScore)
            : LookupCohortLevel(leadershipScore);

    /// <summary>The follower-count counterpart of <see cref="LookupCohortLevelFor"/>.</summary>
    public static FollowerCounts LookupFollowerCountsFor(IEnumerable<string> feats, int leadershipScore) =>
        feats.Contains("feat:epic_leadership")
            ? LookupEpicFollowerCounts(leadershipScore)
            : LookupFollowerCounts(leadershipScore);

    public static FollowerCounts LookupFollowerCounts(int leadershipScore)
    {
        // Below 10 → no followers attracted.
        return leadershipScore switch
        {
            <= 9 => new FollowerCounts(),
            10 => new FollowerCounts { Level1 = 5 },
            11 => new FollowerCounts { Level1 = 6 },
            12 => new FollowerCounts { Level1 = 8 },
            13 => new FollowerCounts { Level1 = 10, Level2 = 1 },
            14 => new FollowerCounts { Level1 = 15, Level2 = 1 },
            15 => new FollowerCounts { Level1 = 20, Level2 = 2, Level3 = 1 },
            16 => new FollowerCounts { Level1 = 25, Level2 = 2, Level3 = 1 },
            17 => new FollowerCounts { Level1 = 30, Level2 = 3, Level3 = 1 },
            18 => new FollowerCounts { Level1 = 35, Level2 = 3, Level3 = 1, Level4 = 1 },
            19 => new FollowerCounts { Level1 = 40, Level2 = 4, Level3 = 2, Level4 = 1, Level5 = 1 },
            20 => new FollowerCounts { Level1 = 50, Level2 = 5, Level3 = 3, Level4 = 2, Level5 = 1 },
            21 => new FollowerCounts { Level1 = 60, Level2 = 6, Level3 = 3, Level4 = 2, Level5 = 1, Level6 = 1 },
            22 => new FollowerCounts { Level1 = 75, Level2 = 7, Level3 = 4, Level4 = 2, Level5 = 2, Level6 = 1 },
            23 => new FollowerCounts { Level1 = 90, Level2 = 9, Level3 = 5, Level4 = 3, Level5 = 2, Level6 = 1 },
            24 => new FollowerCounts { Level1 = 110, Level2 = 11, Level3 = 6, Level4 = 3, Level5 = 2, Level6 = 1 },
            _ => new FollowerCounts { Level1 = 135, Level2 = 13, Level3 = 7, Level4 = 4, Level5 = 2, Level6 = 2 }
        };
    }

    /// <summary>
    /// Table: Epic Leadership. Only reachable with the Epic Leadership feat; below score 25 it
    /// defers to the base table, which the epic table's own first row reproduces.
    /// </summary>
    public static FollowerCounts LookupEpicFollowerCounts(int leadershipScore)
    {
        if (leadershipScore < 25) return LookupFollowerCounts(leadershipScore);
        if (leadershipScore > 40) return ExtrapolateBeyond40(leadershipScore);

        return leadershipScore switch
        {
            25 => Row(135, 13, 7, 4, 2, 2, 1),
            26 => Row(160, 16, 8, 4, 2, 2, 1),
            27 => Row(190, 19, 10, 5, 3, 2, 1),
            28 => Row(220, 22, 11, 6, 3, 2, 1),
            29 => Row(260, 26, 13, 7, 4, 2, 1),
            30 => Row(300, 30, 15, 8, 4, 2, 1),
            31 => Row(350, 35, 18, 9, 5, 3, 2, 1),
            32 => Row(400, 40, 20, 10, 5, 3, 2, 1),
            33 => Row(460, 46, 23, 12, 6, 3, 2, 1),
            34 => Row(520, 52, 26, 13, 6, 3, 2, 1),
            35 => Row(590, 59, 30, 15, 8, 4, 2, 1),
            36 => Row(660, 66, 33, 17, 9, 5, 3, 2, 1),
            37 => Row(740, 74, 37, 19, 10, 5, 3, 2, 1),
            38 => Row(820, 82, 41, 21, 11, 6, 3, 2, 1),
            39 => Row(910, 91, 46, 23, 12, 6, 3, 2, 1),
            _ => Row(1000, 100, 50, 25, 13, 7, 4, 2, 1),
        };
    }

    /// <summary>
    /// Past score 40 the table gives rules instead of rows: "The number of 1st-level followers
    /// increases by 100 for every point of Leadership above 40", then "one-tenth as many 2nd-level
    /// followers as 1st-level … one-half as many 3rd-level as 2nd-level, and so on (round fractions
    /// up, except any fraction less than 1 rounds to 0)".
    ///
    /// "And so on" does not stop where the printed table does: the halving keeps yielding
    /// followers as the score climbs, so a score of 60 fields 11th- and 12th-level followers. The
    /// only ceiling is "A character can't have a follower of higher than 20th level".
    ///
    /// Applying these rules to 1,000 reproduces the printed score-40 row exactly, which is what
    /// says the reading is right.
    /// </summary>
    private static FollowerCounts ExtrapolateBeyond40(int leadershipScore)
    {
        var counts = new int[FollowerCounts.MaxFollowerLevel];
        counts[0] = 1000 + 100 * (leadershipScore - 40);
        counts[1] = RoundUpOrZero(counts[0], 10);
        for (var level = 2; level < counts.Length; level++)
            counts[level] = RoundUpOrZero(counts[level - 1], 2);

        return Row(counts);
    }

    /// <summary>Rounds up, except that anything short of a whole follower is no follower at all.</summary>
    private static int RoundUpOrZero(int value, int divisor) =>
        value < divisor ? 0 : (value + divisor - 1) / divisor;

    /// <summary>Builds a row from counts given in follower-level order, dropping empty levels.</summary>
    private static FollowerCounts Row(params int[] byLevel)
    {
        var counts = new FollowerCounts();
        for (var i = 0; i < byLevel.Length && i < FollowerCounts.MaxFollowerLevel; i++)
        {
            if (byLevel[i] > 0)
                counts.ByLevel[i + 1] = byLevel[i];
        }
        return counts;
    }
}
