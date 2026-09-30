using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>
/// The editing session behind the built-in editor: brush, fill bucket and eraser on a 64×64 skin, with
/// undo/redo. A mouse drag is one stroke (one undo step). UI-free so it can be tested.
/// </summary>
public sealed class SkinEditor
{
    const int MaxUndo = 300;

    readonly List<RgbaImage> _undo = [], _redo = [];
    readonly RgbaImage _start;
    RgbaImage? _strokeStart;

    /// <summary>The game's own look for this hero.</summary>
    public RgbaImage Original { get; }
    public RgbaImage Skin { get; private set; }

    /// <summary>Raised after every change, including each square of a drag, and when a stroke ends.</summary>
    public event Action? Changed;

    public SkinEditor(RgbaImage original, RgbaImage? start = null)
    {
        Original = original.Clone();
        Skin = (start ?? original).Clone();
        _start = Skin.Clone();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Nothing the game reads differs from the game's own look.</summary>
    public bool Unchanged => SkinImport.Unchanged(Original, Skin);

    static readonly HashSet<(int, int)> MayBeEmptySet = BuildMayBeEmpty();

    static HashSet<(int, int)> BuildMayBeEmpty()
    {
        var set = new HashSet<(int, int)>(SkinGeometry.AnimationTexels());
        foreach (var f in SkinGeometry.Faces.Where(f => f.Part == Part.Hat))
            for (int r = 0; r < f.H; r++)
                for (int c = 0; c < f.W; c++)
                    set.Add(f.Texel(c, r));
        return set;
    }

    /// <summary>The hat layer and the face animation may have empty squares; every other part must stay solid.</summary>
    public static bool MayBeEmpty(int tx, int ty) => MayBeEmptySet.Contains((tx, ty));

    public void BeginStroke() => _strokeStart ??= Skin.Clone();

    public void EndStroke()
    {
        if (_strokeStart == null) return;
        var start = _strokeStart;
        _strokeStart = null;
        if (start.SameAs(Skin)) return;
        _undo.Add(start);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>The skin differs from what the session started with (unsaved work).</summary>
    public bool HasEdits => !Skin.SameAs(_start);

    /// <summary>Brush: one square. Returns false when it already had that colour.</summary>
    public bool Paint(int tx, int ty, uint colour) => Set(tx, ty, colour);

    /// <summary>Eraser: empties a square of the hat or face animation; on solid parts it puts back the game's colour.</summary>
    public bool Erase(int tx, int ty) => Set(tx, ty, MayBeEmpty(tx, ty) ? 0 : Original.Get(tx, ty));

    /// <summary>Fill bucket: recolours <paramref name="start"/> and every touching square of the same colour
    /// on the page. Returns the number of squares changed.</summary>
    public int Fill(EditorPage page, EditorCell start, uint colour)
    {
        uint target = Skin.Get(start.Tx, start.Ty);
        if (target == colour) return 0;
        var seen = new HashSet<EditorCell> { start };
        var todo = new Queue<EditorCell>([start]);
        bool own = _strokeStart == null;
        BeginStroke();
        int n = 0;
        while (todo.Count > 0)
        {
            var c = todo.Dequeue();
            Skin.Set(c.Tx, c.Ty, colour);
            n++;
            foreach (var nb in page.Neighbours(c))
                if (RgbaImage.SameColour(Skin.Get(nb.Tx, nb.Ty), target) && seen.Add(nb)) todo.Enqueue(nb);
        }
        if (own) EndStroke();
        Changed?.Invoke();
        return n;
    }

    /// <summary>Back to the game's own look (one undo step).</summary>
    public void Reset()
    {
        if (Skin.SameAs(Original)) return;
        bool own = _strokeStart == null;
        BeginStroke();
        Skin = Original.Clone();
        if (own) EndStroke();
        Changed?.Invoke();
    }

    public void Undo()
    {
        EndStroke();
        if (_undo.Count == 0) return;
        _redo.Add(Skin);
        Skin = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        EndStroke();
        if (_redo.Count == 0) return;
        _undo.Add(Skin);
        Skin = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke();
    }

    bool Set(int tx, int ty, uint colour)
    {
        if (Skin.Get(tx, ty) == colour) return false;
        bool own = _strokeStart == null;
        BeginStroke();
        Skin.Set(tx, ty, colour);
        if (own) EndStroke();
        Changed?.Invoke();
        return true;
    }
}
