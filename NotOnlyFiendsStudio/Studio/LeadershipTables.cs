using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// DMG p.106 Leadership table. Maps a Leadership score to follower counts
/// at follower levels 1-6. Master's cap on cohort level is computed separately
/// as min(LeadershipScore - 2, MasterTotalHD - 2).
/// </summary>
public static class LeadershipTables
{
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
            17 => new FollowerCounts { Level1 = 30, Level2 = 3, Level3 = 1, Level4 = 1 },
            18 => new FollowerCounts { Level1 = 35, Level2 = 3, Level3 = 1, Level4 = 1 },
            19 => new FollowerCounts { Level1 = 40, Level2 = 4, Level3 = 2, Level4 = 1, Level5 = 1 },
            20 => new FollowerCounts { Level1 = 50, Level2 = 5, Level3 = 3, Level4 = 2, Level5 = 1 },
            21 => new FollowerCounts { Level1 = 60, Level2 = 6, Level3 = 3, Level4 = 2, Level5 = 1 },
            22 => new FollowerCounts { Level1 = 75, Level2 = 7, Level3 = 4, Level4 = 2, Level5 = 2, Level6 = 1 },
            23 => new FollowerCounts { Level1 = 90, Level2 = 9, Level3 = 5, Level4 = 3, Level5 = 2, Level6 = 1 },
            24 => new FollowerCounts { Level1 = 110, Level2 = 11, Level3 = 6, Level4 = 3, Level5 = 2, Level6 = 1 },
            _ => new FollowerCounts { Level1 = 135, Level2 = 13, Level3 = 7, Level4 = 4, Level5 = 2, Level6 = 1 }
        };
    }
}
