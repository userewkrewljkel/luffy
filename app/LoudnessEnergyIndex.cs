using System;

namespace LufsTaskbarMeter;

// Exact ordered energy aggregates for BS.1770 integrated gating.
// Unlike rescanning the entire session on every UI refresh, both inserting
// a 100 ms gated block and querying the relative gate take O(log n) time.
// No quantization or time window is introduced: all qualifying blocks count.
internal sealed class LoudnessEnergyIndex
{
    private sealed class Node
    {
        internal readonly double Key;
        internal Node? Left, Right;
        internal int Height = 1;
        internal long Count = 1, SubtreeCount = 1;
        internal double SubtreeEnergy;
        internal Node(double key) { Key = key; SubtreeEnergy = key; }
    }

    private Node? root;
    private static int Height(Node? n) => n?.Height ?? 0;
    private static long Count(Node? n) => n?.SubtreeCount ?? 0;
    private static double Energy(Node? n) => n?.SubtreeEnergy ?? 0;

    private static void Refresh(Node n)
    {
        n.Height = Math.Max(Height(n.Left), Height(n.Right)) + 1;
        n.SubtreeCount = Count(n.Left) + n.Count + Count(n.Right);
        n.SubtreeEnergy = Energy(n.Left) + n.Key * n.Count + Energy(n.Right);
    }

    private static Node RotateRight(Node a)
    {
        Node b = a.Left!;
        a.Left = b.Right;
        b.Right = a;
        Refresh(a);
        Refresh(b);
        return b;
    }

    private static Node RotateLeft(Node a)
    {
        Node b = a.Right!;
        a.Right = b.Left;
        b.Left = a;
        Refresh(a);
        Refresh(b);
        return b;
    }

    private static Node Insert(Node? node, double value)
    {
        if (node == null) return new Node(value);
        if (value < node.Key) node.Left = Insert(node.Left, value);
        else if (value > node.Key) node.Right = Insert(node.Right, value);
        else node.Count++;

        Refresh(node);
        int balance = Height(node.Left) - Height(node.Right);
        if (balance > 1)
        {
            if (value > node.Left!.Key) node.Left = RotateLeft(node.Left);
            return RotateRight(node);
        }
        if (balance < -1)
        {
            if (value < node.Right!.Key) node.Right = RotateRight(node.Right);
            return RotateLeft(node);
        }
        return node;
    }

    internal void Add(double energy)
    {
        if (!double.IsFinite(energy) || energy < 0)
            throw new ArgumentOutOfRangeException(nameof(energy));
        root = Insert(root, energy);
    }

    internal void Clear() => root = null;

    internal (long Count, double Sum) AtOrAbove(double threshold)
    {
        long count = 0;
        double sum = 0;
        var current = root;
        while (current != null)
        {
            if (current.Key >= threshold)
            {
                count += current.Count + Count(current.Right);
                sum += current.Key * current.Count + Energy(current.Right);
                current = current.Left;
            }
            else current = current.Right;
        }
        return (count, sum);
    }

    // All inserted energies have already passed the -70 LUFS absolute gate.
    // The relative gate is 10 LU below their arithmetic mean (energy / 10).
    internal double? Integrated(double absoluteFloor, double offset)
    {
        if (root == null || root.SubtreeCount == 0) return null;
        double relativeGate = Math.Max(absoluteFloor,
            (root.SubtreeEnergy / root.SubtreeCount) / 10.0);
        var (count, sum) = AtOrAbove(relativeGate);
        return count > 0 && sum > 0 ? offset + 10 * Math.Log10(sum / count) : null;
    }
}
