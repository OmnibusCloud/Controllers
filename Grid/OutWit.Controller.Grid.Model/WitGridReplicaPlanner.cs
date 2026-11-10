using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace OutWit.Controller.Grid.Model;

/// <summary>
/// Decides which items of a Grid.ForEach call run more than once. The selection is random and the
/// executions are shuffled, so neither a task's position nor its return slot tells a node whether
/// the item it holds is being run elsewhere too.
/// </summary>
public static class WitGridReplicaPlanner
{
    #region Constants

    /// <summary>The most executions one item can get in a call.</summary>
    public const int MAX_REPLICAS = 3;

    private const double SHARE_ROUNDING_TOLERANCE = 1E-09;

    #endregion

    #region Functions

    /// <summary>
    /// Plans the executions of a call with the platform's random source.
    /// </summary>
    /// <param name="itemCount">How many items the call has.</param>
    /// <param name="replicas">How many times a selected item should run.</param>
    /// <param name="share">The share of the items to select, 0 to 1; rounded up to a whole item.</param>
    /// <param name="machineCount">How many different machines the call can use; fewer machines mean fewer copies.</param>
    /// <returns>The item index of every execution in shuffled order, or null when nothing runs twice.</returns>
    public static IReadOnlyList<int>? Plan(int itemCount, int replicas, double share, int machineCount)
    {
        return Plan(itemCount, replicas, share, machineCount, RandomNumberGenerator.GetInt32);
    }

    /// <summary>
    /// Plans the executions of a call with the given random source.
    /// </summary>
    /// <param name="itemCount">How many items the call has.</param>
    /// <param name="replicas">How many times a selected item should run.</param>
    /// <param name="share">The share of the items to select, 0 to 1; rounded up to a whole item.</param>
    /// <param name="machineCount">How many different machines the call can use; fewer machines mean fewer copies.</param>
    /// <param name="nextInt">Returns a uniform integer from 0 up to, not including, its argument.</param>
    /// <returns>The item index of every execution in shuffled order, or null when nothing runs twice.</returns>
    public static IReadOnlyList<int>? Plan(int itemCount, int replicas, double share, int machineCount, Func<int, int> nextInt)
    {
        int copies = Math.Min(Math.Min(replicas, MAX_REPLICAS), machineCount);

        if (copies <= 1 || itemCount <= 0 || !(share > 0))
            return null;

        int selected = SelectedCount(itemCount, share);

        int[] items = Enumerable.Range(0, itemCount).ToArray();
        Shuffle(items, nextInt);

        var slots = new int[itemCount + selected * (copies - 1)];
        Array.Copy(items, slots, itemCount);

        int position = itemCount;
        for (int item = 0; item < selected; item++)
        {
            for (int copy = 1; copy < copies; copy++)
                slots[position++] = items[item];
        }

        Shuffle(slots, nextInt);

        return slots;
    }

    private static int SelectedCount(int itemCount, double share)
    {
        int selected = (int)Math.Ceiling(itemCount * Math.Min(share, 1.0) - SHARE_ROUNDING_TOLERANCE);

        return Math.Clamp(selected, 1, itemCount);
    }

    private static void Shuffle(int[] values, Func<int, int> nextInt)
    {
        for (int last = values.Length - 1; last > 0; last--)
        {
            int other = nextInt(last + 1);
            (values[last], values[other]) = (values[other], values[last]);
        }
    }

    #endregion
}
