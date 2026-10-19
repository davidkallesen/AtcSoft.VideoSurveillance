namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// Finds the connected groups (8-connected) of a mask's set pixels, in two passes with
/// union-find, and merges groups that lie close together into one object.
/// </summary>
internal static class ConnectedComponents
{
    /// <summary>
    /// Finds the blobs of a mask.
    /// </summary>
    public static List<Blob> Find(
        byte[] mask,
        int width,
        int height)
    {
        var labels = new int[mask.Length];
        var parent = new List<int> { 0 };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width) + x;
                if (mask[i] == 0)
                {
                    continue;
                }

                // The neighbours already labelled: left, up-left, up, up-right.
                var label = 0;
                label = Join(parent, label, x > 0 ? labels[i - 1] : 0);
                if (y > 0)
                {
                    var up = i - width;
                    label = Join(parent, label, x > 0 ? labels[up - 1] : 0);
                    label = Join(parent, label, labels[up]);
                    label = Join(parent, label, x < width - 1 ? labels[up + 1] : 0);
                }

                if (label == 0)
                {
                    label = parent.Count;
                    parent.Add(label);
                }

                labels[i] = label;
            }
        }

        // Resolve every label to its root once, then gather the boxes in arrays by root.
        var roots = new int[parent.Count];
        for (var label = 1; label < parent.Count; label++)
        {
            roots[label] = Root(parent, label);
        }

        var minX = new int[parent.Count];
        var minY = new int[parent.Count];
        var maxX = new int[parent.Count];
        var maxY = new int[parent.Count];
        var count = new int[parent.Count];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var label = labels[row + x];
                if (label == 0)
                {
                    continue;
                }

                var root = roots[label];
                if (count[root] == 0)
                {
                    minX[root] = maxX[root] = x;
                    minY[root] = maxY[root] = y;
                }
                else
                {
                    minX[root] = Math.Min(minX[root], x);
                    maxX[root] = Math.Max(maxX[root], x);
                    maxY[root] = y;
                }

                count[root]++;
            }
        }

        var blobs = new List<Blob>();
        for (var root = 1; root < parent.Count; root++)
        {
            if (count[root] > 0)
            {
                blobs.Add(new Blob(minX[root], minY[root], maxX[root], maxY[root], count[root]));
            }
        }

        return blobs;
    }

    /// <summary>
    /// Merges blobs within a gap of each other until none are, so the fragments of one object
    /// become one box.
    /// </summary>
    public static List<Blob> Merge(
        List<Blob> blobs,
        int gap)
    {
        var result = new List<Blob>(blobs);
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var a = 0; a < result.Count && !merged; a++)
            {
                for (var b = a + 1; b < result.Count; b++)
                {
                    if (result[a].IsNear(result[b], gap))
                    {
                        result[a] = result[a].Union(result[b]);
                        result.RemoveAt(b);
                        merged = true;
                        break;
                    }
                }
            }
        }

        return result;
    }

    private static int Join(
        List<int> parent,
        int label,
        int neighbour)
    {
        if (neighbour == 0)
        {
            return label;
        }

        var root = Root(parent, neighbour);
        if (label == 0)
        {
            return root;
        }

        var own = Root(parent, label);
        if (own != root)
        {
            parent[Math.Max(own, root)] = Math.Min(own, root);
        }

        return Math.Min(own, root);
    }

    private static int Root(
        List<int> parent,
        int label)
    {
        while (parent[label] != label)
        {
            parent[label] = parent[parent[label]];
            label = parent[label];
        }

        return label;
    }
}