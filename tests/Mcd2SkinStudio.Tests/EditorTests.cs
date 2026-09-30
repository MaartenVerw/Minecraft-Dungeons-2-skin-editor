using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Tests;

public class EditorTests
{
    static readonly uint Red = RgbaImage.Pack(255, 0, 0, 255), Blue = RgbaImage.Pack(0, 0, 255, 255);

    /// <summary>A synthetic skin (not game art): a plain colour per part, transparent hat and brows.</summary>
    static RgbaImage Skin()
    {
        var img = new RgbaImage(64, 64);
        img.FillRect(0, 0, 64, 64, RgbaImage.Pack(90, 60, 30, 255));
        foreach (var f in SkinGeometry.Faces)
            for (int r = 0; r < f.H; r++)
                for (int c = 0; c < f.W; c++)
                {
                    var (x, y) = f.Texel(c, r);
                    img.Set(x, y, f.Part == Part.Hat ? 0 : RgbaImage.Pack((byte)(20 + 30 * (int)f.Part), 100, 100, 255));
                }
        img.FillRect(SkinGeometry.BrowX, SkinGeometry.BrowY, SkinGeometry.BrowW, SkinGeometry.BrowH, 0);
        return img;
    }

    [Fact]
    public void Every_texel_the_game_reads_is_on_exactly_one_page_once()
    {
        var all = EditorLayout.AllCells.Select(c => (c.Tx, c.Ty)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(SkinImport.UsedTexels().ToHashSet(), all.ToHashSet());
    }

    [Theory]
    [InlineData(EditorView.Head)]
    [InlineData(EditorView.Hat)]
    [InlineData(EditorView.Body)]
    [InlineData(EditorView.Arms)]
    [InlineData(EditorView.Legs)]
    [InlineData(EditorView.Face)]
    public void Page_squares_fit_and_never_overlap(EditorView v)
    {
        var page = EditorLayout.Page(v);
        var covered = new HashSet<(int, int)>();
        foreach (var c in page.Cells)
        {
            Assert.InRange(c.X + c.Size, 1, page.Width);
            Assert.InRange(c.Y + c.Size, 1, page.Height);
            for (int y = c.Y; y < c.Y + c.Size; y++)
                for (int x = c.X; x < c.X + c.Size; x++)
                    Assert.True(covered.Add((x, y)), $"{v}: overlap at {x},{y}");
            Assert.Same(c, page.CellAt(c.X + c.Size / 2.0, c.Y + c.Size / 2.0));
        }
    }

    [Fact]
    public void Arms_page_shows_the_heros_right_arm_on_the_left()
    {
        var page = EditorLayout.Page(EditorView.Arms);
        var right = SkinGeometry.Get(Part.RightArm, Face.Front);
        var left = SkinGeometry.Get(Part.LeftArm, Face.Front);
        double X((int X, int Y) t) => page.Cells.Single(c => (c.Tx, c.Ty) == t).X;
        Assert.True(X(right.Texel(0, 0)) < X(left.Texel(0, 0)));
    }

    [Fact]
    public void Brush_paints_one_square_and_a_drag_is_one_undo_step()
    {
        var ed = new SkinEditor(Skin());
        int changes = 0;
        ed.Changed += () => changes++;
        ed.BeginStroke();
        Assert.True(ed.Paint(20, 20, Red));
        Assert.True(ed.Paint(21, 20, Red));
        Assert.False(ed.Paint(21, 20, Red));
        ed.EndStroke();
        Assert.Equal(3, changes);   // two squares, then the end of the stroke
        Assert.True(ed.HasEdits);
        Assert.Equal(Red, ed.Skin.Get(20, 20));
        Assert.Equal(Skin().Get(22, 20), ed.Skin.Get(22, 20));
        ed.Undo();
        Assert.True(ed.Skin.SameAs(Skin()));
        Assert.False(ed.CanUndo);
        ed.Redo();
        Assert.Equal(Red, ed.Skin.Get(21, 20));
    }

    [Fact]
    public void Eraser_empties_hat_and_face_animation_but_restores_solid_parts()
    {
        var ed = new SkinEditor(Skin());
        ed.Paint(40, 8, Red);                 // hat
        ed.Paint(20, 20, Red);                // body front
        ed.Paint(SkinGeometry.PupilA.X, SkinGeometry.PupilA.Y, Red);
        ed.Paint(SkinGeometry.PortraitX, SkinGeometry.PortraitY, Red);
        ed.Erase(40, 8);
        ed.Erase(20, 20);
        ed.Erase(SkinGeometry.PupilA.X, SkinGeometry.PupilA.Y);
        ed.Erase(SkinGeometry.PortraitX, SkinGeometry.PortraitY);
        Assert.Equal(0u, ed.Skin.Get(40, 8));
        Assert.Equal(Skin().Get(20, 20), ed.Skin.Get(20, 20));
        Assert.Equal(0u, ed.Skin.Get(SkinGeometry.PupilA.X, SkinGeometry.PupilA.Y));
        Assert.Equal(Skin().Get(SkinGeometry.PortraitX, SkinGeometry.PortraitY), ed.Skin.Get(SkinGeometry.PortraitX, SkinGeometry.PortraitY));
        Assert.True(SkinEditor.MayBeEmpty(SkinGeometry.BrowX, SkinGeometry.BrowY));
        Assert.False(SkinEditor.MayBeEmpty(8, 8));
    }

    [Fact]
    public void Fill_stays_inside_the_touching_same_colour_area_of_one_page()
    {
        var skin = Skin();
        var ed = new SkinEditor(skin);
        var page = EditorLayout.Page(EditorView.Body);
        // a blue belt all the way round the body splits the page in two
        var front = SkinGeometry.Get(Part.Body, Face.Front);
        ed.BeginStroke();
        foreach (var side in new[] { Face.Right, Face.Front, Face.Left, Face.Back })
        {
            var m = SkinGeometry.Get(Part.Body, side);
            for (int c = 0; c < m.W; c++) ed.Paint(m.Texel(c, 6).X, m.Texel(c, 6).Y, Blue);
        }
        ed.EndStroke();
        var start = page.Cells.Single(c => (c.Tx, c.Ty) == front.Texel(0, 0));
        int n = ed.Fill(page, start, Red);
        Assert.Equal(Red, ed.Skin.Get(front.Texel(7, 5).X, front.Texel(7, 5).Y));      // above the line
        Assert.Equal(Red, ed.Skin.Get(SkinGeometry.Get(Part.Body, Face.Top).Texel(0, 0).X, SkinGeometry.Get(Part.Body, Face.Top).Texel(0, 0).Y));
        Assert.NotEqual(Red, ed.Skin.Get(front.Texel(0, 7).X, front.Texel(0, 7).Y));   // below the line
        Assert.Equal(Blue, ed.Skin.Get(front.Texel(3, 6).X, front.Texel(3, 6).Y));
        Assert.Equal(skin.Get(44, 20), ed.Skin.Get(44, 20));                               // arm: other page
        Assert.True(n > 8);
        ed.Undo();
        Assert.Equal(skin.Get(front.Texel(0, 0).X, front.Texel(0, 0).Y), ed.Skin.Get(front.Texel(0, 0).X, front.Texel(0, 0).Y));
    }

    [Fact]
    public void Fill_does_not_spill_from_eyebrows_into_the_eyes()
    {
        var skin = Skin();
        skin.FillRect(2, 5, 6, 3, 0);  // whole animation corner empty
        var ed = new SkinEditor(skin);
        var page = EditorLayout.Page(EditorView.Face);
        var brow = page.Cells.First(c => (c.Tx, c.Ty) == (SkinGeometry.BrowX, SkinGeometry.BrowY));
        Assert.Equal(8, ed.Fill(page, brow, Red));
        Assert.Equal(0u, ed.Skin.Get(SkinGeometry.WhiteOuter.X, SkinGeometry.WhiteOuter.Y));
    }

    [Fact]
    public void Start_over_and_edit_again()
    {
        var mine = Skin();
        mine.Set(20, 20, Blue);
        var ed = new SkinEditor(Skin(), mine);
        Assert.False(ed.Unchanged);
        ed.Reset();
        Assert.True(ed.Unchanged);
        ed.Undo();
        Assert.Equal(Blue, ed.Skin.Get(20, 20));
    }

    [Fact]
    public void Preview_locates_every_front_and_back_texel()
    {
        // body front top-left square is at (4, 8) of the front view
        var f = SkinGeometry.Get(Part.Body, Face.Front);
        Assert.Contains((true, 4, 8), SkinRender.Where(f.Texel(0, 0).X, f.Texel(0, 0).Y));
        // a pupil shows on the face, the mirrored brow on both sides
        Assert.Contains((true, 4 + 2, 4), SkinRender.Where(SkinGeometry.PupilA.X, SkinGeometry.PupilA.Y));
        var brow = SkinRender.Where(SkinGeometry.BrowX, SkinGeometry.BrowY).ToList();
        Assert.Contains((true, 4, 2), brow);
        Assert.Contains((true, 11, 2), brow);
        // every painted front/back pixel is found where Front()/Back() draw it
        var skin = Skin();
        foreach (var p in new[] { Part.Head, Part.Body, Part.RightArm, Part.LeftArm, Part.RightLeg, Part.LeftLeg })
            foreach (var face in new[] { Face.Front, Face.Back })
            {
                var map = SkinGeometry.Get(p, face);
                var (tx, ty) = map.Texel(1, 1);
                var probe = skin.Clone();
                probe.Set(tx, ty, Red);
                var view = face == Face.Front ? SkinRender.Front(probe) : SkinRender.Back(probe);
                var spots = SkinRender.Where(tx, ty).Where(w => w.Front == (face == Face.Front)).ToList();
                Assert.NotEmpty(spots);
                Assert.All(spots, s => Assert.Equal(Red, view.Get(s.X, s.Y)));
            }
        Assert.Equal(Part.Body, SkinRender.PartAt(true, 6, 12));
        Assert.Equal(Part.RightArm, SkinRender.PartAt(true, 2, 12));
        Assert.Equal(Part.RightArm, SkinRender.PartAt(false, 13, 12));
        Assert.Equal(Part.Head, SkinRender.PartAt(false, 6, 3));
        Assert.Null(SkinRender.PartAt(true, 0, 0));
    }
}
