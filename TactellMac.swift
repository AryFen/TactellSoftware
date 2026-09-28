import Cocoa
import ApplicationServices

class Bookmark {
    let element: AXUIElement
    let displayName: String
    
    init(element: AXUIElement, displayName: String) {
        self.element = element
        self.displayName = displayName
    }
}

class ExcelNavigator {
    private var currentLayer: AXUIElement?
    private var currentLayerElements: [AXUIElement?] = []
    private var currentIndex: Int = 0
    private var navigationStack: [(layer: AXUIElement, index: Int)] = []
    private var excelApp: NSRunningApplication?
    private var activatedElements: Set<UnsafeMutableRawPointer> = []
    private let lockQueue = DispatchQueue(label: "com.excelnavigator.lock")
    private var pendingInteractionElement: AXUIElement?
    private var dynamicContentTimer: Timer?
    private var bookmarks: [Bookmark] = []
    private var currentBookmarkIndex: Int = -1
    private var observers: [AXObserver] = []
    
    init() {
        initializeExcelWindow()
        registerGlobalEventListeners()
    }
    
    func toggleBookmark() {
        lockQueue.sync {
            guard currentLayerElements.count > 0 && currentIndex < currentLayerElements.count else {
                print("No element to bookmark")
                return
            }
            
            guard let currentElement = currentLayerElements[currentIndex] else {
                print("Cannot bookmark the GoTo Cell A1 option")
                return
            }
            
            for i in 0..<bookmarks.count {
                if CFEqual(bookmarks[i].element, currentElement) {
                    print("Removed bookmark: \(bookmarks[i].displayName)")
                    bookmarks.remove(at: i)
                    
                    if currentBookmarkIndex >= bookmarks.count {
                        currentBookmarkIndex = bookmarks.count > 0 ? 0 : -1
                    }
                    return
                }
            }
            
            let displayName = formatElementForDisplay(currentElement)
            let newBookmark = Bookmark(element: currentElement, displayName: displayName)
            bookmarks.append(newBookmark)
            
            print("Added bookmark [\(bookmarks.count)]: \(displayName)")
        }
    }
    
    func nextBookmark() {
        lockQueue.sync {
            guard bookmarks.count > 0 else {
                print("No bookmarks saved")
                return
            }
            
            if bookmarks.count == 1 {
                navigateToBookmark(0)
                return
            }
            
            var currentElement: AXUIElement? = nil
            if currentIndex < currentLayerElements.count {
                currentElement = currentLayerElements[currentIndex]
            }
            
            var nextIndex = -1
            
            var i = 0
            while i < bookmarks.count {
                if let current = currentElement, CFEqual(bookmarks[i].element, current) {
                    nextIndex = (i + 1) % bookmarks.count
                    break
                }
                i += 1
            }
            
            if nextIndex == -1 {
                nextIndex = 0
            }
            
            navigateToBookmark(nextIndex)
        }
    }
    
    func listBookmarks() {
        lockQueue.sync {
            guard bookmarks.count > 0 else {
                print("No bookmarks saved")
                return
            }
            
            print("\n=== Bookmarks (\(bookmarks.count)) ===")
            for i in 0..<bookmarks.count {
                print("[\(i + 1)] \(bookmarks[i].displayName)")
            }
            print()
        }
    }
    
    private func navigateToBookmark(_ bookmarkIndex: Int) {
        guard bookmarkIndex >= 0 && bookmarkIndex < bookmarks.count else { return }
        
        let bookmark = bookmarks[bookmarkIndex]
        
        for i in 0..<currentLayerElements.count {
            if let element = currentLayerElements[i], CFEqual(element, bookmark.element) {
                currentIndex = i
                displayCurrentElement()
                currentBookmarkIndex = bookmarkIndex
                return
            }
        }
        
        if let parent = getParent(bookmark.element) {
            if navigateToElementLayer(parent) {
                for i in 0..<currentLayerElements.count {
                    if let element = currentLayerElements[i], CFEqual(element, bookmark.element) {
                        currentIndex = i
                        displayCurrentElement()
                        currentBookmarkIndex = bookmarkIndex
                        return
                    }
                }
            }
        }
        
        print("Could not navigate to bookmark: \(bookmark.displayName)")
    }
    
    private func navigateToElementLayer(_ targetElement: AXUIElement) -> Bool {
        if let parent = getParent(targetElement) {
            loadLayer(parent)
            return true
        }
        return false
    }
    
    func next() {
        lockQueue.sync {
            guard currentLayerElements.count > 0 else {
                print("No elements in current layer")
                return
            }
            
            currentIndex = (currentIndex + 1) % currentLayerElements.count
            displayCurrentElement()
        }
    }
    
    func previous() {
        lockQueue.sync {
            guard currentLayerElements.count > 0 else {
                print("No elements in current layer")
                return
            }
            
            currentIndex = (currentIndex - 1 + currentLayerElements.count) % currentLayerElements.count
            displayCurrentElement()
        }
    }
    
    func select() {
        lockQueue.sync {
            guard currentLayerElements.count > 0 && currentIndex < currentLayerElements.count else {
                print("No element to select")
                return
            }
            
            if let element = currentLayerElements[currentIndex] {
                interactWithElement(element)
            } else {
                goToCellA1()
            }
        }
    }
    
    func back() {
        lockQueue.sync {
            guard !navigationStack.isEmpty else {
                print("At root level")
                return
            }
            
            let (prevLayer, prevIndex) = navigationStack.removeLast()
            loadLayer(prevLayer)
            currentIndex = prevIndex
            displayCurrentElement()
        }
    }
    
    private func initializeExcelWindow() {
        let workspace = NSWorkspace.shared
        let runningApps = workspace.runningApplications
        
        if let excel = runningApps.first(where: { $0.bundleIdentifier == "com.microsoft.Excel" }) {
            excelApp = excel
            if let window = getExcelMainWindow() {
                loadLayer(window)
                displayCurrentElement()
            } else {
                print("Could not find Excel window")
            }
        } else {
            print("Excel not found. Please open Excel.")
        }
    }
    
    private func getExcelMainWindow() -> AXUIElement? {
        guard let excelApp = excelApp else { return nil }
        
        let appElement = AXUIElementCreateApplication(excelApp.processIdentifier)
        
        var windowsValue: AnyObject?
        let result = AXUIElementCopyAttributeValue(appElement, kAXWindowsAttribute as CFString, &windowsValue)
        
        guard result == .success, let windows = windowsValue as? [AXUIElement], !windows.isEmpty else {
            return nil
        }
        
        return windows[0]
    }
    
    private func loadLayer(_ element: AXUIElement) {
        navigationStack.append((currentLayer ?? element, currentIndex))
        currentLayer = element
        currentLayerElements = []
        currentIndex = 0
        
        var childrenValue: AnyObject?
        let result = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &childrenValue)
        
        if result == .success, let children = childrenValue as? [AXUIElement] {
            for child in children {
                if isInteractiveElement(child) {
                    currentLayerElements.append(child)
                }
            }
        }
        
        currentLayerElements.insert(nil, at: 0)
    }
    
    private func displayCurrentElement() {
        guard currentIndex < currentLayerElements.count else { return }
        
        if let element = currentLayerElements[currentIndex] {
            let description = formatElementForDisplay(element)
            print("[\(currentIndex)/\(currentLayerElements.count - 1)] \(description)")
        } else {
            print("[0/\(currentLayerElements.count - 1)] GoTo, Cell A1")
        }
    }
    
    private func formatElementForDisplay(_ element: AXUIElement) -> String {
        var roleValue: AnyObject?
        var nameValue: AnyObject?
        
        AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &roleValue)
        AXUIElementCopyAttributeValue(element, kAXTitleAttribute as CFString, &nameValue)
        
        if nameValue == nil {
            AXUIElementCopyAttributeValue(element, kAXDescriptionAttribute as CFString, &nameValue)
        }
        if nameValue == nil {
            AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &nameValue)
        }
        
        let name = (nameValue as? String) ?? "Unnamed"
        let role = roleValue as? String ?? ""
        
        let typeTag = getControlTypeTag(role)
        
        if typeTag.isEmpty {
            return name
        } else {
            return "\(typeTag), \(name)"
        }
    }
    
    private func getControlTypeTag(_ role: String) -> String {
        switch role {
        case kAXButtonRole as String: return "Button"
        case kAXImageRole as String: return "Image"
        case kAXGroupRole as String: return "Group"
        case kAXTextFieldRole as String: return "Edit"
        case kAXComboBoxRole as String: return "ComboBox"
        case kAXCheckBoxRole as String: return "CheckBox"
        case kAXRadioButtonRole as String: return "Radio"
        case kAXTabGroupRole as String: return "Tab"
        case kAXMenuItemRole as String: return "Menu"
        case kAXListRole as String: return "List"
        case kAXTableRole as String: return "Table"
        default: return ""
        }
    }
    
    private func isInteractiveElement(_ element: AXUIElement) -> Bool {
        var roleValue: AnyObject?
        AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &roleValue)
        
        guard let role = roleValue as? String else { return false }
        
        let interactiveRoles = [
            kAXButtonRole as String,
            kAXTextFieldRole as String,
            kAXComboBoxRole as String,
            kAXCheckBoxRole as String,
            kAXRadioButtonRole as String,
            kAXMenuItemRole as String,
            kAXListRole as String,
            kAXTableRole as String,
            kAXGroupRole as String
        ]
        
        return interactiveRoles.contains(role)
    }
    
    private func interactWithElement(_ element: AXUIElement) {
        var childrenValue: AnyObject?
        let childResult = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &childrenValue)
        
        if childResult == .success, let children = childrenValue as? [AXUIElement], !children.isEmpty {
            navigationStack.append((currentLayer!, currentIndex))
            loadLayer(element)
            displayCurrentElement()
            return
        }
        
        var actionNames: CFArray?
        let actionsResult = AXUIElementCopyActionNames(element, &actionNames)
        
        if actionsResult == .success, let actions = actionNames as? [String] {
            if actions.contains(kAXPressAction as String) {
                AXUIElementPerformAction(element, kAXPressAction as CFString)
                print("Activated element")
            } else if actions.contains(kAXPickAction as String) {
                AXUIElementPerformAction(element, kAXPickAction as CFString)
                print("Picked element")
            }
        }
    }
    
    private func goToCellA1() {
        print("Going to Cell A1...")
        
        guard let excelApp = excelApp else { return }
        
        let source = """
        tell application "Microsoft Excel"
            activate
            tell active sheet
                select range "A1"
            end tell
        end tell
        """
        
        var error: NSDictionary?
        if let script = NSAppleScript(source: source) {
            script.executeAndReturnError(&error)
            if let error = error {
                print("Error: \(error)")
            } else {
                print("Navigated to Cell A1")
            }
        }
    }
    
    private func getParent(_ element: AXUIElement) -> AXUIElement? {
        var parentValue: AnyObject?
        let result = AXUIElementCopyAttributeValue(element, kAXParentAttribute as CFString, &parentValue)
        return result == .success ? (parentValue as! AXUIElement) : nil
    }
    
    private func registerGlobalEventListeners() {
        guard let excelApp = excelApp else { return }
        
        let appElement = AXUIElementCreateApplication(excelApp.processIdentifier)
        
        var observer: AXObserver?
        let result = AXObserverCreate(excelApp.processIdentifier, { (observer, element, notification, refcon) in
            let navigator = Unmanaged<ExcelNavigator>.fromOpaque(refcon!).takeUnretainedValue()
            navigator.handleAccessibilityEvent(element: element, notification: notification)
        }, &observer)
        
        if result == .success, let obs = observer {
            let selfPointer = Unmanaged.passUnretained(self).toOpaque()
            
            AXObserverAddNotification(obs, appElement, kAXFocusedUIElementChangedNotification as CFString, selfPointer)
            AXObserverAddNotification(obs, appElement, kAXValueChangedNotification as CFString, selfPointer)
            
            CFRunLoopAddSource(CFRunLoopGetCurrent(), AXObserverGetRunLoopSource(obs), .defaultMode)
            observers.append(obs)
        }
    }
    
    private func handleAccessibilityEvent(element: AXUIElement, notification: CFString) {
        let notificationName = notification as String
        
        if notificationName == kAXFocusedUIElementChangedNotification as String {
            lockQueue.async { [weak self] in
                self?.onFocusChanged(element)
            }
        } else if notificationName == kAXValueChangedNotification as String {
            lockQueue.async { [weak self] in
                self?.onValueChanged(element)
            }
        }
    }
    
    private func onFocusChanged(_ element: AXUIElement) {
        var roleValue: AnyObject?
        AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &roleValue)
        
        if let role = roleValue as? String, role == kAXCellRole as String {
            displayCellInfo(element)
        }
    }
    
    private func onValueChanged(_ element: AXUIElement) {
        var roleValue: AnyObject?
        AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &roleValue)
        
        if let role = roleValue as? String, role == kAXCellRole as String {
            displayCellInfo(element)
        }
    }
    
    private func displayCellInfo(_ cell: AXUIElement) {
        var valueValue: AnyObject?
        var positionValue: AnyObject?
        
        AXUIElementCopyAttributeValue(cell, kAXValueAttribute as CFString, &valueValue)
        AXUIElementCopyAttributeValue(cell, kAXPositionAttribute as CFString, &positionValue)
        
        let value = (valueValue as? String) ?? ""
        
        var rowValue: AnyObject?
        var colValue: AnyObject?
        AXUIElementCopyAttributeValue(cell, "AXRowIndexRange" as CFString, &rowValue)
        AXUIElementCopyAttributeValue(cell, "AXColumnIndexRange" as CFString, &colValue)
        
        print("Cell: \(value)")
    }
    
    func cleanup() {
        dynamicContentTimer?.invalidate()
        observers.removeAll()
    }
}

class GlobalKeyMonitor {
    private var monitor: Any?
    var keyPressed: ((CGKeyCode, NSEvent.ModifierFlags) -> Void)?
    
    init() {
        setupMonitor()
    }
    
    private func setupMonitor() {
        monitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let keyCode = event.keyCode
            let modifiers = event.modifierFlags
            
            if modifiers.contains([.control, .option]) {
                self?.keyPressed?(keyCode, modifiers)
            } else if [123, 124, 125, 126].contains(keyCode) {
                self?.keyPressed?(keyCode, modifiers)
            }
        }
    }
    
    func cleanup() {
        if let monitor = monitor {
            NSEvent.removeMonitor(monitor)
        }
    }
}

class AppDelegate: NSObject, NSApplicationDelegate {
    var navigator: ExcelNavigator?
    var keyMonitor: GlobalKeyMonitor?
    var statusItem: NSStatusItem?
    
    func applicationDidFinishLaunching(_ notification: Notification) {
        checkAccessibilityPermissions()
        
        print("=== Excel Navigator with Global Hotkeys ===")
        print("Hotkeys (Excel must be open):")
        print("  Ctrl+Opt+N - Next")
        print("  Ctrl+Opt+P - Previous")
        print("  Ctrl+Opt+S - Select")
        print("  Ctrl+Opt+B - Back")
        print("  Arrow Keys - Navigate cells and update display")
        print("  Ctrl+Opt+T - Toggle Bookmark (add/remove)")
        print("  Ctrl+Opt+M - Next Bookmark")
        print("  Ctrl+Opt+L - List Bookmarks")
        print("  Ctrl+Opt+Q - Quit")
        print("\nKeep Excel focused. Press Ctrl+Opt+Q to exit.\n")
        
        navigator = ExcelNavigator()
        keyMonitor = GlobalKeyMonitor()
        
        keyMonitor?.keyPressed = { [weak self] keyCode, modifiers in
            self?.handleKeyPress(keyCode: keyCode, modifiers: modifiers)
        }
        
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem?.button {
            button.title = "Excel Nav"
        }
        
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        statusItem?.menu = menu
        
        print("Navigator ready. Listening for hotkeys...\n")
    }
    
    private func checkAccessibilityPermissions() {
        let options: NSDictionary = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true]
        let accessEnabled = AXIsProcessTrustedWithOptions(options)
        
        if !accessEnabled {
            print("Accessibility permissions required!")
            print("Please grant accessibility access in System Preferences > Security & Privacy > Accessibility")
        }
    }
    
    private func handleKeyPress(keyCode: CGKeyCode, modifiers: NSEvent.ModifierFlags) {
        let hasCtrl = modifiers.contains(.control)
        let hasOpt = modifiers.contains(.option)
        
        if hasCtrl && hasOpt {
            switch keyCode {
            case 45:
                print("\n>>> NEXT")
                navigator?.next()
            case 35:
                print("\n>>> PREVIOUS")
                navigator?.previous()
            case 1:
                print("\n>>> SELECT")
                navigator?.select()
            case 11:
                print("\n>>> BACK")
                navigator?.back()
            case 17:
                print("\n>>> TOGGLE BOOKMARK")
                navigator?.toggleBookmark()
            case 46:
                print("\n>>> NEXT BOOKMARK")
                navigator?.nextBookmark()
            case 37:
                print("\n>>> LIST BOOKMARKS")
                navigator?.listBookmarks()
            case 12:
                print("\n>>> QUIT")
                NSApplication.shared.terminate(nil)
            default:
                break
            }
        } else if !hasCtrl && !hasOpt {
            switch keyCode {
            case 126, 125, 123, 124:
                break
            default:
                break
            }
        }
    }
    
    func applicationWillTerminate(_ notification: Notification) {
        navigator?.cleanup()
        keyMonitor?.cleanup()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
