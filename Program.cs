using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace BraillePadNavigator
{
    public class ExcelNavigator
    {
        private AutomationElement currentLayer;
        private List<AutomationElement> currentLayerElements;
        private int currentIndex;
        private Stack<(AutomationElement layer, int index)> navigationStack;
        private AutomationElement excelWindow;
        private HashSet<AutomationElement> activatedElements;
        private object lockObject = new object();
        private AutomationElement pendingInteractionElement;
        private ManualResetEvent dynamicContentEvent;
        private System.Threading.Timer dynamicContentTimer;
        private List<Bookmark> bookmarks;
        private int currentBookmarkIndex;

        private class Bookmark
        {
            public AutomationElement Element { get; set; }
            public string DisplayName { get; set; }

            public Bookmark(AutomationElement element, string displayName)
            {
                Element = element;
                DisplayName = displayName;
            }
        }

        public ExcelNavigator()
        {
            currentLayerElements = new List<AutomationElement>();
            currentIndex = 0;
            navigationStack = new Stack<(AutomationElement, int)>();
            activatedElements = new HashSet<AutomationElement>();
            dynamicContentEvent = new ManualResetEvent(false);
            bookmarks = new List<Bookmark>();
            currentBookmarkIndex = -1;
            

            InitializeExcelWindow();
            RegisterGlobalEventListeners();
        }

        public void ToggleBookmark()
        {
            lock (lockObject)
            {
                if (currentLayerElements.Count == 0 || currentIndex >= currentLayerElements.Count)
                {
                    Console.WriteLine("No element to bookmark");
                    return;
                }

                AutomationElement currentElement = currentLayerElements[currentIndex];

                if (currentElement == null)
                {
                    Console.WriteLine("Cannot bookmark the GoTo Cell A1 option");
                    return;
                }

                for (int i = 0; i < bookmarks.Count; i++)
                {
                    if (bookmarks[i].Element.Equals(currentElement))
                    {
                        Console.WriteLine($"Removed bookmark: {bookmarks[i].DisplayName}");
                        bookmarks.RemoveAt(i);

                        if (currentBookmarkIndex >= bookmarks.Count)
                        {
                            currentBookmarkIndex = bookmarks.Count > 0 ? 0 : -1;
                        }

                        return;
                    }
                }

                string displayName = FormatElementForDisplay(currentElement);

                Bookmark newBookmark = new Bookmark(currentElement, displayName);
                bookmarks.Add(newBookmark);

                Console.WriteLine($"Added bookmark [{bookmarks.Count}]: {displayName}");
            }
        }

        public void NextBookmark()
        {
            lock (lockObject)
            {
                if (bookmarks.Count == 0)
                {
                    Console.WriteLine("No bookmarks saved");
                    return;
                }

                if (bookmarks.Count == 1)
                {
                    // Only one bookmark, just navigate to it
                    NavigateToBookmark(0);
                    return;
                }

                // Check if we're currently on a bookmarked element
                AutomationElement currentElement = null;
                if (currentIndex < currentLayerElements.Count)
                {
                    currentElement = currentLayerElements[currentIndex];
                }

                int nextIndex = -1;

                // Find if current element is a bookmark
                for (int i = 0; i < bookmarks.Count; i++)
                {
                    try
                    {
                        if (currentElement != null && bookmarks[i].Element.Equals(currentElement))
                        {
                            // We're on a bookmark, go to next one
                            nextIndex = (i + 1) % bookmarks.Count;
                            break;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                        // This bookmark is no longer valid, remove it
                        Console.WriteLine($"Removing invalid bookmark: {bookmarks[i].DisplayName}");
                        bookmarks.RemoveAt(i);
                        i--;
                    }
                }

                // If we're not on a bookmark, go to the first one
                if (nextIndex == -1)
                {
                    nextIndex = 0;
                }

                NavigateToBookmark(nextIndex);
            }
        }

        
        public void ListBookmarks()
        {
            lock (lockObject)
            {
                if (bookmarks.Count == 0)
                {
                    Console.WriteLine("No bookmarks saved");
                    return;
                }

                Console.WriteLine($"\n=== Bookmarks ({bookmarks.Count}) ===");
                for (int i = 0; i < bookmarks.Count; i++)
                {
                    Console.WriteLine($"[{i + 1}] {bookmarks[i].DisplayName}");
                }
                Console.WriteLine();
            }
        }

        private void NavigateToBookmark(int bookmarkIndex)
        {
            if (bookmarkIndex < 0 || bookmarkIndex >= bookmarks.Count)
                return;

            try
            {
                Bookmark bookmark = bookmarks[bookmarkIndex];

                // Verify element still exists
                string testName = bookmark.Element.Current.Name;

                // First, check if it's in the current layer (fast path)
                for (int i = 0; i < currentLayerElements.Count; i++)
                {
                    if (currentLayerElements[i] != null && currentLayerElements[i].Equals(bookmark.Element))
                    {
                        currentIndex = i;
                        DisplayCurrentElement();
                        currentBookmarkIndex = bookmarkIndex;
                        return;
                    }
                }

                // Not in current layer - find its parent and navigate there
                AutomationElement parent = TreeWalker.RawViewWalker.GetParent(bookmark.Element);

                if (parent == null)
                {
                    Console.WriteLine("Could not find bookmark");
                    return;
                }

                // Navigate to the parent's layer silently
                if (NavigateToElementLayer(parent))
                {
                    // Now find the bookmark in current layer
                    for (int i = 0; i < currentLayerElements.Count; i++)
                    {
                        if (currentLayerElements[i] != null && currentLayerElements[i].Equals(bookmark.Element))
                        {
                            currentIndex = i;
                            DisplayCurrentElement();
                            currentBookmarkIndex = bookmarkIndex;
                            return;
                        }
                    }

                    Console.WriteLine("Could not find bookmark");
                }
                else
                {
                    Console.WriteLine("Could not find bookmark");
                }

            }
            catch (ElementNotAvailableException)
            {
                Console.WriteLine($"Bookmark no longer exists: {bookmarks[bookmarkIndex].DisplayName}");
                bookmarks.RemoveAt(bookmarkIndex);

                if (bookmarks.Count > 0)
                {
                    NavigateToBookmark(bookmarkIndex % bookmarks.Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error navigating to bookmark: {ex.Message}");
            }
        }

        private bool NavigateToElementLayer(AutomationElement targetLayer)
        {
            try
            {
                // Go back to root silently
                while (navigationStack.Count > 0)
                {
                    var (previousLayer, previousIndex) = navigationStack.Pop();
                    currentLayer = previousLayer;
                    currentIndex = previousIndex;
                    RefreshCurrentLayer();
                }

                // Search from root to find path to target layer
                return SearchAndNavigateToLayer(excelWindow, targetLayer, new List<AutomationElement>());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool SearchAndNavigateToLayer(AutomationElement currentSearchRoot, AutomationElement target, List<AutomationElement> pathSoFar)
        {
            try
            {
                // Check if current search root is the target
                if (currentSearchRoot.Equals(target))
                {
                    // We found it! Navigate the path we've built silently
                    foreach (var element in pathSoFar)
                    {
                        // Find this element in current layer
                        for (int i = 0; i < currentLayerElements.Count; i++)
                        {
                            if (currentLayerElements[i] != null && currentLayerElements[i].Equals(element))
                            {
                                currentIndex = i;

                                // Navigate into it silently
                                AutomationElementCollection directChildren = element.FindAll(
                                    TreeScope.Children,
                                    Condition.TrueCondition
                                );

                                if (directChildren.Count > 0)
                                {
                                    AutomationElement targetElement = SkipSingleChildContainers(element);

                                    // Navigate without displaying
                                    navigationStack.Push((currentLayer, currentIndex));
                                    currentLayer = targetElement;
                                    currentIndex = 0;
                                    RefreshCurrentLayer();

                                    Thread.Sleep(50);
                                }
                                break;
                            }
                        }
                    }
                    return true;
                }

                // Search children
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement child = walker.GetFirstChild(currentSearchRoot);

                while (child != null)
                {
                    pathSoFar.Add(child);

                    if (SearchAndNavigateToLayer(child, target, pathSoFar))
                    {
                        return true;
                    }

                    pathSoFar.RemoveAt(pathSoFar.Count - 1);
                    child = walker.GetNextSibling(child);
                }

                return false;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }


        private void InitializeExcelWindow()
        {
            AutomationElement desktop = AutomationElement.RootElement;
            PropertyCondition condition = new PropertyCondition(
                AutomationElement.ClassNameProperty,
                "XLMAIN"
            );

            excelWindow = desktop.FindFirst(TreeScope.Children, condition);

            if (excelWindow == null)
            {
                throw new InvalidOperationException("Microsoft Excel window not found.");
            }

            currentLayer = excelWindow;
            RefreshCurrentLayer();
        }

        private void RegisterGlobalEventListeners()
        {
            Automation.AddStructureChangedEventHandler(
                excelWindow,
                TreeScope.Subtree,
                OnStructureChanged
            );

            Automation.AddAutomationPropertyChangedEventHandler(
                excelWindow,
                TreeScope.Subtree,
                OnPropertyChanged,
                AutomationElement.IsSelectionItemPatternAvailableProperty,
                SelectionItemPattern.IsSelectedProperty
            );

            Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);

            Automation.AddAutomationEventHandler(
                AutomationElement.LayoutInvalidatedEvent,
                excelWindow,
                TreeScope.Subtree,
                OnLayoutInvalidated
            );

            Automation.AddAutomationEventHandler(
                AutomationElement.MenuOpenedEvent,
                excelWindow,
                TreeScope.Subtree,
                OnMenuOpened
            );
        }

        private void OnStructureChanged(object sender, StructureChangedEventArgs e)
        {
            lock (lockObject)
            {
                if (pendingInteractionElement != null)
                {
                    AutomationElement element = sender as AutomationElement;
                    if (element != null && IsContainerType(element) && HasChildren(element))
                    {
                        activatedElements.Add(element);
                        dynamicContentEvent.Set();
                    }
                }
            }
        }

        private void OnPropertyChanged(object sender, AutomationPropertyChangedEventArgs e)
        {
            lock (lockObject)
            {
                if (pendingInteractionElement != null &&
                    e.Property == SelectionItemPattern.IsSelectedProperty)
                {
                    AutomationElement element = sender as AutomationElement;
                    if (element != null && (bool)e.NewValue == true)
                    {
                        if (IsContainerType(element) && HasChildren(element))
                        {
                            activatedElements.Add(element);
                            dynamicContentEvent.Set();
                        }
                    }
                }
            }
        }

        private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            lock (lockObject)
            {
                if (pendingInteractionElement != null)
                {
                    AutomationElement element = AutomationElement.FocusedElement;
                    if (element != null && IsContainerType(element) && HasChildren(element))
                    {
                        activatedElements.Add(element);
                        dynamicContentEvent.Set();
                    }
                }
            }
        }

        private void OnLayoutInvalidated(object sender, AutomationEventArgs e)
        {
            lock (lockObject)
            {
                if (pendingInteractionElement != null)
                {
                    AutomationElement element = sender as AutomationElement;
                    if (element != null && IsContainerType(element) && HasChildren(element))
                    {
                        activatedElements.Add(element);
                        dynamicContentEvent.Set();
                    }
                }
            }
        }

        private void OnMenuOpened(object sender, AutomationEventArgs e)
        {
            lock (lockObject)
            {
                if (pendingInteractionElement != null)
                {
                    AutomationElement element = sender as AutomationElement;
                    if (element != null && IsContainerType(element) && HasChildren(element))
                    {
                        activatedElements.Add(element);
                        dynamicContentEvent.Set();
                    }
                }
            }
        }

        public void Next()
        {
            lock (lockObject)
            {
                if (currentLayerElements.Count == 0) return;

                currentIndex = (currentIndex + 1) % currentLayerElements.Count;
                DisplayCurrentElement();
            }
        }

        public void Previous()
        {
            lock (lockObject)
            {
                if (currentLayerElements.Count == 0) return;

                currentIndex = (currentIndex - 1 + currentLayerElements.Count) % currentLayerElements.Count;
                DisplayCurrentElement();
            }
        }

        public void Select()
        {
            lock (lockObject)
            {
                try
                {
                    if (currentLayerElements.Count == 0 || currentIndex >= currentLayerElements.Count)
                        return;

                    AutomationElement selectedElement = currentLayerElements[currentIndex];

                    if (selectedElement == null)
                    {
                        GoToCellA1();
                        return;
                    }

                    pendingInteractionElement = selectedElement;
                    activatedElements.Clear();
                    dynamicContentEvent.Reset();

                    AutomationElementCollection directChildren = selectedElement.FindAll(
                        TreeScope.Children,
                        Condition.TrueCondition
                    );

                    if (directChildren.Count > 0)
                    {
                        for (int i = 0; i < Math.Min(directChildren.Count, 5); i++)
                        {
                            continue;
                        }
                    }

                    if (directChildren.Count > 0)
                    {
                        AutomationElement targetElement = SkipSingleChildContainers(selectedElement);

                        NavigateToChildren(targetElement);
                        pendingInteractionElement = null;
                        return;
                    }

                    if (IsContainerType(selectedElement))
                    {
                        InvokeElement(selectedElement);

                        dynamicContentTimer = new System.Threading.Timer(
                            _ => { },
                            null,
                            Timeout.Infinite,
                            Timeout.Infinite
                        );

                        bool eventFired = dynamicContentEvent.WaitOne(100);

                        if (activatedElements.Count > 0)
                        {
                            AutomationElement targetElement = FindShallowestParent(activatedElements);

                            NavigateToChildren(targetElement);
                            dynamicContentTimer?.Dispose();
                            pendingInteractionElement = null;
                            return;
                        }

                        Thread.Sleep(150);
                        directChildren = selectedElement.FindAll(TreeScope.Children, Condition.TrueCondition);

                        if (directChildren.Count > 0)
                        {
                            for (int i = 0; i < Math.Min(directChildren.Count, 3); i++)
                            {
                                continue;
                            }
                            NavigateToChildren(selectedElement);
                            dynamicContentTimer?.Dispose();
                            pendingInteractionElement = null;
                            return;
                        }

                        if (selectedElement.Current.ControlType == ControlType.MenuItem)
                        {
                            AutomationElement popupContent = FindMenuPopupContent(selectedElement);

                            if (popupContent != null)
                            {
                                NavigateToChildren(popupContent);
                                dynamicContentTimer?.Dispose();
                                pendingInteractionElement = null;
                                return;
                            }
                        }

                        AutomationElement contentArea = FindAssociatedContent(selectedElement);

                        if (contentArea != null)
                        {
                            NavigateToChildren(contentArea);
                            dynamicContentTimer?.Dispose();
                            pendingInteractionElement = null;
                            return;
                        }
                        else
                        {

                        }

                        dynamicContentTimer = new System.Threading.Timer(
                            _ => FallbackSearch(),
                            null,
                            0,
                            Timeout.Infinite
                        );

                        bool fallbackFired = dynamicContentEvent.WaitOne(2500);
                        dynamicContentTimer?.Dispose();

                        if (activatedElements.Count > 0)
                        {
                            AutomationElement targetElement = FindShallowestParent(activatedElements);

                            NavigateToChildren(targetElement);
                        }
                        else
                        {

                        }
                    }
                    else
                    {
                        List<string> dialogsBefore = GetDialogWindowsInExcelTree();

                        InvokeElement(selectedElement);
                        Thread.Sleep(200);
                        List<string> dialogsAfter = GetDialogWindowsInExcelTree();

                        var newDialogs = dialogsAfter.Except(dialogsBefore).ToList();

                        if (newDialogs.Count > 0)
                        {
                            AutomationElement newDialog = FindDialogInExcelTree(newDialogs[0]);
                            if (newDialog != null && HasChildren(newDialog))
                            {
                                NavigateToChildren(newDialog);
                                pendingInteractionElement = null;
                                return;
                            }
                        }

                        int previousCount = currentLayerElements.Count;
                        RefreshCurrentLayer();

                        if (currentLayerElements.Count > previousCount)
                        {
                            if (currentIndex < currentLayerElements.Count)
                            {
                                DisplayCurrentElement();
                            }
                        }
                    }

                    pendingInteractionElement = null;

                }
                catch (Exception)
                {
                    pendingInteractionElement = null;
                }
            }
        }

        public void Back()
        {
            lock (lockObject)
            {
                if (navigationStack.Count == 0) return;

                while (navigationStack.Count > 0)
                {
                    var (previousLayer, previousIndex) = navigationStack.Pop();

                    try
                    {
                        AutomationElementCollection testChildren = previousLayer.FindAll(
                            TreeScope.Children,
                            Condition.TrueCondition
                        );

                        if (testChildren.Count > 0)
                        {
                            currentLayer = previousLayer;
                            currentIndex = previousIndex;
                            RefreshCurrentLayer();
                            DisplayCurrentElement();
                            return;
                        }
                        else
                        {
                        }
                    }
                    catch (ElementNotAvailableException)
                    {

                    }
                }
            }
        }

        private AutomationElement SkipSingleChildContainers(AutomationElement element)
        {
            try
            {
                AutomationElement current = element;
                int skipCount = 0;
                const int maxSkips = 10;

                while (skipCount < maxSkips)
                {
                    AutomationElementCollection children = current.FindAll(
                        TreeScope.Children,
                        Condition.TrueCondition
                    );

                    if (children.Count == 1)
                    {
                        AutomationElement onlyChild = children[0];

                        if (IsContainerType(onlyChild))
                        {
                            current = onlyChild;
                            skipCount++;
                            continue;
                        }
                    }
                    break;
                }

                return current;
            }
            catch (ElementNotAvailableException)
            {
                return element;
            }
        }

        private void NavigateToChildren(AutomationElement element)
        {
            navigationStack.Push((currentLayer, currentIndex));

            currentLayer = element;
            currentIndex = 0;
            RefreshCurrentLayer();
            DisplayCurrentElement();
        }

        private void RefreshCurrentLayer()
        {
            currentLayerElements.Clear();

            // Add a special "GoTo Cell A1" option at the front of EVERY layer
            currentLayerElements.Add(null); // null represents the special GoTo action

            try
            {
                AutomationElementCollection children = currentLayer.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition
                );

                foreach (AutomationElement child in children)
                {
                    currentLayerElements.Add(child);
                }
            }
            catch (ElementNotAvailableException)
            {

            }
        }

        private void FallbackSearch()
        {
            lock (lockObject)
            {
                if (activatedElements.Count > 0 || pendingInteractionElement == null)
                    return;

                string containerName = GetElementName(pendingInteractionElement);

                var matchingContainers = FindContainersWithName(excelWindow, containerName);

                if (matchingContainers.Count > 0)
                {
                    activatedElements.UnionWith(matchingContainers);
                    dynamicContentEvent.Set();
                }
            }
        }

        private List<AutomationElement> FindContainersWithName(AutomationElement root, string name)
        {
            var results = new List<AutomationElement>();

            if (string.IsNullOrEmpty(name))
                return results;

            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement element = walker.GetFirstChild(root);

                while (element != null)
                {
                    string elementName = GetElementName(element);

                    if (!string.IsNullOrEmpty(elementName) &&
                        elementName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        IsContainerType(element) &&
                        HasChildren(element))
                    {
                        results.Add(element);
                    }

                    results.AddRange(FindContainersWithName(element, name));

                    element = walker.GetNextSibling(element);
                }
            }
            catch (ElementNotAvailableException)
            {

            }

            return results;
        }

        private AutomationElement FindShallowestParent(HashSet<AutomationElement> elements)
        {
            if (elements.Count == 0) return null;
            if (elements.Count == 1) return elements.First();

            AutomationElement shallowest = null;
            int minDepth = int.MaxValue;

            foreach (var candidate in elements)
            {
                bool containsAll = true;
                int depth = GetTreeDepth(candidate);

                foreach (var other in elements)
                {
                    if (candidate.Equals(other)) continue;

                    if (!IsAncestorOf(candidate, other))
                    {
                        containsAll = false;
                        break;
                    }
                }

                if (containsAll && depth < minDepth)
                {
                    shallowest = candidate;
                    minDepth = depth;
                }
            }

            return shallowest ?? elements.First();
        }

        private bool IsAncestorOf(AutomationElement ancestor, AutomationElement descendant)
        {
            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement current = descendant;

                while (current != null)
                {
                    current = walker.GetParent(current);
                    if (current != null && current.Equals(ancestor))
                        return true;
                }
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }

            return false;
        }

        private int GetTreeDepth(AutomationElement element)
        {
            int depth = 0;
            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement current = element;

                while (current != null && !current.Equals(AutomationElement.RootElement))
                {
                    current = walker.GetParent(current);
                    depth++;
                }
            }
            catch (ElementNotAvailableException)
            {

            }

            return depth;
        }

        private void GoToCellA1()
        {
            try
            {

                // Find all DataItem controls (Excel cells)
                PropertyCondition cellCondition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.DataItem
                );

                AutomationElementCollection cells = excelWindow.FindAll(TreeScope.Descendants, cellCondition);


                foreach (AutomationElement cell in cells)
                {
                    string cellName = GetElementName(cell);

                    if (cellName == "A1")
                    {

                        // Set focus to the cell
                        cell.SetFocus();

                        Thread.Sleep(100);

                        // Get cell value and format type
                        string cellValue = "";
                        string cellType = "Unknown";

                        if (cell.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                        {
                            cellValue = ((ValuePattern)pattern).Current.Value;
                        }

                        // Try to determine cell type from properties
                        try
                        {
                            // Check if it's a number, date, text, etc.
                            if (string.IsNullOrEmpty(cellValue))
                            {
                                cellType = "Empty";
                            }
                            else if (double.TryParse(cellValue, out _))
                            {
                                cellType = "Number";
                            }
                            else if (DateTime.TryParse(cellValue, out _))
                            {
                                cellType = "Date";
                            }
                            else
                            {
                                cellType = "Text";
                            }

                            // Try to get the actual format from Excel's item type
                            string itemType = cell.Current.ItemType;
                            if (!string.IsNullOrEmpty(itemType))
                            {
                                cellType = itemType;
                            }
                        }
                        catch
                        {
                            cellType = "General";
                        }

                        Console.WriteLine($"hello, Cell: A1, Type: General");
                        if (!string.IsNullOrEmpty(cellValue)) { cell.SetFocus(); }
                        { cell.SetFocus();
                        }

                        return;
                    }
                }
            }
            catch (Exception )
            {
            }
        }

        private void DisplayCurrentCellInfo()
        {
            try
            {
                // First try to get the focused element directly
                AutomationElement focusedElement = AutomationElement.FocusedElement;

                if (focusedElement != null && focusedElement.Current.ControlType == ControlType.DataItem)
                {
                    string name = GetElementName(focusedElement);
                    string value = "";

                    if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    {
                        value = ((ValuePattern)pattern).Current.Value;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                    }
                    return;
                }

                // If focused element isn't a DataItem, search for cells with keyboard focus
                PropertyCondition cellCondition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.DataItem
                );
                PropertyCondition focusCondition = new PropertyCondition(
                    AutomationElement.HasKeyboardFocusProperty,
                    true
                );
                AndCondition condition = new AndCondition(cellCondition, focusCondition);

                AutomationElement focusedCell = excelWindow.FindFirst(TreeScope.Descendants, condition);

                if (focusedCell != null)
                {
                    string name = GetElementName(focusedCell);
                    string value = "";

                    if (focusedCell.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    {
                        value = ((ValuePattern)pattern).Current.Value;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception )
            {
            }
        }

        public void UpdateCurrentCellDisplay()
        {
            lock (lockObject)
            {
                DisplayCurrentCellInfo();
            }
        }

        private AutomationElement FindMenuPopupContent(AutomationElement menuItem)
        {
            try
            {
                AutomationElementCollection descendants = menuItem.FindAll(
                    TreeScope.Descendants,
                    Condition.TrueCondition
                );

                foreach (AutomationElement desc in descendants)
                {
                    ControlType descType = desc.Current.ControlType;
                    string descName = GetElementName(desc);

                    if (descType != ControlType.Menu &&
                        descType != ControlType.MenuItem &&
                        HasChildren(desc))
                    {
                        if ((descType == ControlType.Group || descType == ControlType.Pane) &&
                            !string.IsNullOrEmpty(descName))
                        {
                            return desc;
                        }
                    }
                }

                foreach (AutomationElement desc in descendants)
                {
                    ControlType descType = desc.Current.ControlType;

                    if (descType != ControlType.Menu &&
                        descType != ControlType.MenuItem &&
                        HasChildren(desc))
                    {
                        return desc;
                    }
                }

                AutomationElement desktop = AutomationElement.RootElement;
                AutomationElementCollection windows = desktop.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition
                );

                foreach (AutomationElement window in windows)
                {
                    string windowName = GetElementName(window);
                    ControlType windowType = window.Current.ControlType;

                    if ((windowType == ControlType.Window || windowType == ControlType.Pane) &&
                        HasChildren(window) &&
                        !window.Current.IsOffscreen)
                    {
                        if (!string.IsNullOrEmpty(windowName))
                        {
                            return window;
                        }
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            return null;
        }

        private AutomationElement FindAssociatedContent(AutomationElement sourceElement)
        {
            try
            {
                string sourceName = GetElementName(sourceElement);

                if (string.IsNullOrEmpty(sourceName))
                    return null;

                var visibleContainers = FindVisibleContainersWithContent(excelWindow);

                foreach (var container in visibleContainers)
                {
                    if (IsAncestorOf(container, sourceElement))
                    {
                        continue;
                    }

                    string containerName = GetElementName(container);

                    if (!string.IsNullOrEmpty(containerName) &&
                        containerName.IndexOf(sourceName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        HasChildren(container))
                    {
                        return container;
                    }
                }

            }
            catch (ElementNotAvailableException)
            {

            }

            return null;
        }

        private List<AutomationElement> FindVisibleContainersWithContent(AutomationElement root)
        {
            var results = new List<AutomationElement>();

            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement element = walker.GetFirstChild(root);

                while (element != null)
                {
                    if (IsContainerType(element) &&
                        !element.Current.IsOffscreen &&
                        HasChildren(element))
                    {
                        results.Add(element);
                    }

                    results.AddRange(FindVisibleContainersWithContent(element));

                    element = walker.GetNextSibling(element);
                }
            }
            catch (ElementNotAvailableException)
            {

            }

            return results;
        }

        private List<string> GetDialogWindowsInExcelTree()
        {
            var dialogNames = new List<string>();

            try
            {
                AutomationElementCollection windows = excelWindow.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)
                );

                foreach (AutomationElement window in windows)
                {
                    string name = GetElementName(window);
                    if (!string.IsNullOrEmpty(name))
                    {
                        dialogNames.Add(name);
                    }
                }
            }
            catch (ElementNotAvailableException)
            {

            }

            return dialogNames;
        }

        private AutomationElement FindDialogInExcelTree(string dialogName)
        {
            try
            {
                PropertyCondition nameCondition = new PropertyCondition(
                    AutomationElement.NameProperty,
                    dialogName
                );
                PropertyCondition typeCondition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Window
                );
                AndCondition condition = new AndCondition(nameCondition, typeCondition);

                return excelWindow.FindFirst(TreeScope.Descendants, condition);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        private bool IsContainerType(AutomationElement element)
        {
            try
            {
                ControlType controlType = element.Current.ControlType;

                return controlType == ControlType.Group ||
                       controlType == ControlType.Pane ||
                       controlType == ControlType.TabItem ||
                       controlType == ControlType.Menu ||
                       controlType == ControlType.MenuBar ||
                       controlType == ControlType.ToolBar ||
                       controlType == ControlType.Window ||
                       controlType == ControlType.List ||
                       controlType == ControlType.Tree ||
                       controlType == ControlType.Table ||
                       controlType == ControlType.DataGrid;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        private bool HasChildren(AutomationElement element)
        {
            try
            {
                AutomationElement child = TreeWalker.RawViewWalker.GetFirstChild(element);
                return child != null;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        private void InvokeElement(AutomationElement element)
        {
            try
            {
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                {
                    ((InvokePattern)pattern).Invoke();
                }
                else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                {
                    ((SelectionItemPattern)pattern).Select();
                }
                else if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
                {
                    ((ExpandCollapsePattern)pattern).Expand();
                }
            }
            catch (ElementNotAvailableException)
            {

            }
        }

        private string GetElementName(AutomationElement element)
        {
            try
            {
                return element.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
        }

        private void DisplayCurrentElement()
        {
            if (currentLayerElements.Count == 0 || currentIndex >= currentLayerElements.Count)
            {
                return;
            }

            AutomationElement element = currentLayerElements[currentIndex];

            // Check if this is the special GoTo Cell A1 option
            if (element == null)
            {
                string output = $"[{currentIndex + 1}/{currentLayerElements.Count}] GoTo Cell A1";
                Console.WriteLine(output);
                return;
            }

            string display = FormatElementForDisplay(element);
            string output2 = $"[{currentIndex + 1}/{currentLayerElements.Count}] {display}";

            Console.WriteLine(output2);
        }

        private string FormatElementForDisplay(AutomationElement element)
        {
            try
            {
                if (element == null)
                    return "GoTo Cell A1";

                string name = element.Current.Name;
                ControlType controlType = element.Current.ControlType;

                string typeTag = GetControlTypeTag(controlType);

                if (string.IsNullOrEmpty(typeTag))
                {
                    return name;
                }
                else
                {
                    return $"{typeTag}, {name}";
                }
            }
            catch (ElementNotAvailableException)
            {
                return "Element unavailable";
            }
        }

        private string GetControlTypeTag(ControlType controlType)
        {
            if (controlType == ControlType.Button) return "Button";
            if (controlType == ControlType.Image) return "Image";
            if (controlType == ControlType.Group) return "Group";
            if (controlType == ControlType.Edit) return "Edit";
            if (controlType == ControlType.ComboBox) return "ComboBox";
            if (controlType == ControlType.CheckBox) return "CheckBox";
            if (controlType == ControlType.RadioButton) return "Radio";
            if (controlType == ControlType.TabItem) return "Tab";
            if (controlType == ControlType.MenuItem) return "Menu";
            if (controlType == ControlType.List) return "List";
            if (controlType == ControlType.Table) return "Table";
            if (controlType == ControlType.DataGrid) return "DataGrid";

            return string.Empty;
        }

        public void Cleanup()
        {
            Automation.RemoveAllEventHandlers();
            dynamicContentTimer?.Dispose();
            dynamicContentEvent?.Dispose();
        }
    }

    public class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        public event EventHandler<Keys> KeyPressed;

        public GlobalKeyboardHook()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool ctrlPressed = (GetAsyncKeyState(Keys.ControlKey) & 0x8000) != 0;
                bool altPressed = (GetAsyncKeyState(Keys.Menu) & 0x8000) != 0;

                // Handle arrow keys without modifiers
                if (!ctrlPressed && !altPressed)
                {
                    if (key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right)
                    {
                        KeyPressed?.Invoke(this, key);
                        return CallNextHookEx(_hookID, nCode, wParam, lParam); // Let Excel handle the arrow key
                    }
                }

                if (ctrlPressed && altPressed)
                {
                    KeyPressed?.Invoke(this, key);
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys vKey);
    }

    class Program
    {
        private static ExcelNavigator navigator;
        private static GlobalKeyboardHook keyboardHook;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("=== Excel Navigator with Global Hotkeys ===");
                Console.WriteLine("Hotkeys (Excel must be open):");
                Console.WriteLine("  Ctrl+Alt+N - Next");
                Console.WriteLine("  Ctrl+Alt+P - Previous");
                Console.WriteLine("  Ctrl+Alt+S - Select");
                Console.WriteLine("  Ctrl+Alt+B - Back");
                Console.WriteLine("  Arrow Keys - Navigate cells and update display");
                Console.WriteLine("  Ctrl+Alt+T - Toggle Bookmark (add/remove)");
                Console.WriteLine("  Ctrl+Alt+M - Next Bookmark");
                Console.WriteLine("  Ctrl+Alt+L - List Bookmarks");
                Console.WriteLine("  Ctrl+Alt+Q - Quit");
                Console.WriteLine("\nKeep Excel focused. Press Ctrl+Alt+Q to exit.\n");

                navigator = new ExcelNavigator();
                keyboardHook = new GlobalKeyboardHook();
                keyboardHook.KeyPressed += OnGlobalKeyPressed;

                Console.WriteLine("Navigator ready. Listening for hotkeys...\n");

                Application.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.ReadKey();
            }
            finally
            {
                keyboardHook?.Dispose();
                navigator?.Cleanup();
            }
        }

        private static void OnGlobalKeyPressed(object sender, Keys key)
        {
            try
            {
                switch (key)
                {
                    case Keys.N:
                        Console.WriteLine("\n>>> NEXT");
                        navigator.Next();
                        break;
                    case Keys.P:
                        Console.WriteLine("\n>>> PREVIOUS");
                        navigator.Previous();
                        break;
                    case Keys.S:
                        Console.WriteLine("\n>>> SELECT");
                        navigator.Select();
                        break;
                    case Keys.B:
                        Console.WriteLine("\n>>> BACK");
                        navigator.Back();
                        break;
                    case Keys.T:
                        Console.WriteLine("\n>>> TOGGLE BOOKMARK");
                        navigator.ToggleBookmark();
                        break;
                    case Keys.M:
                        Console.WriteLine("\n>>> NEXT BOOKMARK");
                        navigator.NextBookmark();
                        break;
                    case Keys.L:
                        Console.WriteLine("\n>>> LIST BOOKMARKS");
                        navigator.ListBookmarks();
                        break;
                    case Keys.Q:
                        Console.WriteLine("\n>>> QUIT");
                        Application.Exit();
                        break;
                }
            }
            catch (Exception)
            {

            }
        }
    }
}