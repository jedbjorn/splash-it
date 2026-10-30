using System.Collections.ObjectModel;
using SplashIt.Revit.Tabs;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;
using Fake = SplashIt.RuntimeTests.Tabs.FakeAvalonDock;

namespace SplashIt.RuntimeTests.Tabs
{
    /// <summary>
    /// Spec #2 AC2/AC5 support: <see cref="AvalonDockPane"/>, the one reflection boundary, against
    /// fake types shaped like AvalonDock's layout model (<c>LayoutGroup&lt;T&gt;.MoveChild</c> on a
    /// generic base, <c>SelectedContentIndex</c> on the pane, <c>Parent</c> on the element). The
    /// source file is compiled into this project by link (it uses no Revit API), so these tests run
    /// the shipped code. Only Revit itself can prove the real AvalonDock matches (spec #2, AC4).
    /// </summary>
    public class AvalonDockPaneTests
    {
        private static (Fake.LayoutDocumentPane Pane, List<Fake.LayoutDocument> Documents) Pane(string titles, int selected = -1)
        {
            var pane = new Fake.LayoutDocumentPane();
            var documents = titles.Split(' ').Select(t => new Fake.LayoutDocument(t)).ToList();
            foreach (var document in documents) pane.Add(document);
            pane.SelectedContentIndex = selected;
            return (pane, documents);
        }

        [Fact]
        public void Finder_WrapsTheParentPane_AndReadsItThroughReflection()
        {
            var (model, documents) = Pane("A1 B1 A2", selected: 1);
            var log = new ListLog();

            var lookup = new AvalonDockPane.Finder(log).Find(documents);

            Assert.True(lookup.IsAvailable);
            var pane = Assert.IsType<AvalonDockPane>(Assert.Single(lookup.Panes));
            Assert.Same(model, pane.Model);
            Assert.Equal(3, pane.Count);
            Assert.Same(documents[2], pane.ItemAt(2));
            Assert.Same(documents[1], pane.SelectedItem);

            pane.Move(2, 0);
            Assert.Equal(new[] { (2, 0) }, model.MoveChildCalls);
            Assert.Equal(new[] { "A2", "A1", "B1" }, model.Children.Select(c => c.Title));

            pane.SelectedItem = documents[0];
            Assert.Equal(1, model.SelectedContentIndex); // A1 is now at index 1
        }

        [Fact]
        public void Finder_LogsTheAvalonDockVersionAndMoveChild_OncePerFinder()
        {
            var (_, first) = Pane("A1");
            var (_, second) = Pane("B1");
            var log = new ListLog();
            var finder = new AvalonDockPane.Finder(log);

            finder.Find(first);
            finder.Find(second);

            var line = Assert.Single(log.Lines);
            var assembly = typeof(Fake.LayoutDocumentPane).Assembly.GetName();
            Assert.StartsWith($"Info group tabs: first click this session: LayoutDocumentPane from {assembly.Name} {assembly.Version} (file ", line);
            Assert.EndsWith("MoveChild found.", line);
        }

        [Fact]
        public void Finder_ReturnsEachPaneOnce_InTheOrderOfItsFirstTab()
        {
            // Tile Views: two panes, tabs given in surface order.
            var (left, l) = Pane("A1 B1");
            var (right, r) = Pane("C1 A2");

            var lookup = new AvalonDockPane.Finder().Find([r[0], l[0], l[1], r[1], null]);

            Assert.True(lookup.IsAvailable);
            Assert.Equal(new object[] { right, left }, lookup.Panes.Select(p => ((AvalonDockPane)p).Model));
        }

        [Fact]
        public void Finder_AcceptsATypeDerivedFromLayoutDocumentPane()
        {
            var pane = new Fake.DerivedDocumentPane();
            var document = new Fake.LayoutDocument("A1");
            pane.Add(document);

            Assert.True(new AvalonDockPane.Finder().Find([document]).IsAvailable);
        }

        [Fact]
        public void Finder_ADocumentOutsideADocumentPane_MakesTheWholeLookupUnavailable()
        {
            var (_, documents) = Pane("A1 B1");
            var floating = new Fake.LayoutDocument("C1") { Parent = new Fake.LayoutAnchorablePane() };

            var lookup = new AvalonDockPane.Finder().Find([documents[0], floating, documents[1]]);

            Assert.False(lookup.IsAvailable);
            Assert.Empty(lookup.Panes);
            Assert.Equal("A document tab's parent is LayoutAnchorablePane, not LayoutDocumentPane.", lookup.Reason);
        }

        [Fact]
        public void Finder_APaneWithoutMoveChild_IsUnavailable_AndTheLogSaysSo()
        {
            var pane = new Fake.Old.LayoutDocumentPane();
            var document = new Fake.LayoutDocument("A1") { Parent = pane };
            var log = new ListLog();

            var lookup = new AvalonDockPane.Finder(log).Find([document]);

            Assert.False(lookup.IsAvailable);
            Assert.Equal("LayoutDocumentPane has no public MoveChild(int, int).", lookup.Reason);
            var line = Assert.Single(log.Lines);
            Assert.StartsWith("Warn ", line);
            Assert.EndsWith("MoveChild NOT found.", line);
        }

        [Fact]
        public void Finder_APaneWithAReadOnlySelection_IsUnavailable()
        {
            var pane = new Fake.ReadOnlySelection.LayoutDocumentPane();
            var document = new Fake.LayoutDocument("A1") { Parent = pane };

            var lookup = new AvalonDockPane.Finder().Find([document]);

            Assert.Equal("LayoutDocumentPane has no public read-write int SelectedContentIndex.", lookup.Reason);
        }

        [Fact]
        public void Finder_ADocumentWithoutParent_IsUnavailable()
        {
            var lookup = new AvalonDockPane.Finder().Find([new object()]);

            Assert.Equal("Object has no public Parent property.", lookup.Reason);
        }

        [Fact]
        public void Finder_NoDocuments_OrOnlyClosingOnes_IsUnavailable()
        {
            var closing = new Fake.LayoutDocument("A1"); // Parent null: it has left its pane

            Assert.Equal(AvalonDockPane.Finder.NoTabsReason, new AvalonDockPane.Finder().Find([]).Reason);
            Assert.Equal(AvalonDockPane.Finder.NoTabsReason, new AvalonDockPane.Finder().Find([closing]).Reason);
        }

        [Fact]
        public void Finder_ParentThatThrows_IsUnavailable_NotAnException()
        {
            var lookup = new AvalonDockPane.Finder().Find([new Fake.ThrowingDocument()]);

            Assert.Equal("Finding the document panes failed (InvalidOperationException).", lookup.Reason);
        }

        [Fact]
        public void Move_RethrowsWhatMoveChildThrew_NotTheReflectionWrapper()
        {
            var (model, documents) = Pane("A1 B1");
            model.ThrowOnMove = true;
            var pane = new AvalonDockPane.Finder().Find(documents).Panes[0];

            Assert.Throws<InvalidOperationException>(() => pane.Move(0, 1));
        }

        [Fact]
        public void SelectedItem_SettingATabOfAnotherPane_Throws()
        {
            var (_, documents) = Pane("A1 B1", selected: 0);
            var (_, others) = Pane("C1");
            var pane = new AvalonDockPane.Finder().Find(documents).Panes[0];

            Assert.Throws<InvalidOperationException>(() => pane.SelectedItem = others[0]);
            Assert.Null(new AvalonDockPane.Finder().Find(Pane("D1").Documents).Panes[0].SelectedItem);
        }

        /// <summary>The finder and the grouper together, over a fake layout with two tiles.</summary>
        [Fact]
        public void FinderAndGrouper_GroupEachPane_AndKeepTheSelectedDocument()
        {
            var (left, l) = Pane("B1 A1 B2 A2", selected: 2);
            var (right, r) = Pane("A3 C1 A4", selected: 0);
            var log = new ListLog();

            var lookup = new AvalonDockPane.Finder(log).Find(l.Concat(r));
            var result = new TabGrouper(item => ((Fake.LayoutDocument)item).Title[..1], log).Group(lookup.Panes);

            Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
            Assert.Equal(new[] { "B1", "B2", "A1", "A2" }, left.Children.Select(c => c.Title));
            Assert.Equal(new[] { "A3", "A4", "C1" }, right.Children.Select(c => c.Title));
            Assert.Same(l[2], left.Children[left.SelectedContentIndex]);
            Assert.Same(r[0], right.Children[right.SelectedContentIndex]);
            Assert.Equal(2, log.Lines.Count); // the version once, then the result
        }
    }
}

namespace SplashIt.RuntimeTests.Tabs.FakeAvalonDock
{
    // Shaped like AvalonDock's layout model, by member name only (from the public Xceed
    // documentation and Lane B's Revit dumps, not from any source code).

    public abstract class LayoutElement
    {
        public object? Parent { get; set; }
    }

    public abstract class LayoutContent : LayoutElement
    {
        protected LayoutContent(string title) => Title = title;

        public string Title { get; }
    }

    public sealed class LayoutDocument(string title) : LayoutContent(title);

    public abstract class LayoutGroup<T> : LayoutElement
        where T : LayoutElement
    {
        public ObservableCollection<T> Children { get; } = [];

        public List<(int, int)> MoveChildCalls { get; } = [];

        public bool ThrowOnMove { get; set; }

        public void Add(T child)
        {
            Children.Add(child);
            child.Parent = this;
        }

        public void MoveChild(int oldIndex, int newIndex)
        {
            MoveChildCalls.Add((oldIndex, newIndex));
            if (ThrowOnMove) throw new InvalidOperationException("move refused");
            if (oldIndex == newIndex) return;
            Children.Move(oldIndex, newIndex);
        }
    }

    /// <summary>Its selection is an index that a move does not update, so the grouper must restore it.</summary>
    public class LayoutDocumentPane : LayoutGroup<LayoutContent>
    {
        public int SelectedContentIndex { get; set; } = -1;
    }

    public sealed class DerivedDocumentPane : LayoutDocumentPane;

    public sealed class LayoutAnchorablePane : LayoutGroup<LayoutContent>
    {
        public int SelectedContentIndex { get; set; } = -1;
    }

    public sealed class ThrowingDocument
    {
        public object Parent => throw new InvalidOperationException();
    }
}

namespace SplashIt.RuntimeTests.Tabs.FakeAvalonDock.Old
{
    /// <summary>A pane without MoveChild.</summary>
    public sealed class LayoutDocumentPane
    {
        public ObservableCollection<object> Children { get; } = [];

        public int SelectedContentIndex { get; set; }
    }
}

namespace SplashIt.RuntimeTests.Tabs.FakeAvalonDock.ReadOnlySelection
{
    public sealed class LayoutDocumentPane : LayoutGroup<LayoutContent>
    {
        public int SelectedContentIndex => 0;
    }
}
