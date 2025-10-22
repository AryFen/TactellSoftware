import UIKit
import Foundation

class Bookmark {
    let appBundleIdentifier: String?
    let elementDescription: String
    let timestamp: Date
    
    init(appBundleIdentifier: String?, elementDescription: String) {
        self.appBundleIdentifier = appBundleIdentifier
        self.elementDescription = elementDescription
        self.timestamp = Date()
    }
}

class SystemNavigator {
    private var bookmarks: [Bookmark] = []
    private var currentBookmarkIndex: Int = -1
    private let lockQueue = DispatchQueue(label: "com.systemnavigator.lock")
    
    func captureCurrentContext() -> String {
        var context = ""
        
        if let topApp = UIApplication.shared.windows.first?.rootViewController {
            context = "App: \(type(of: topApp))"
        }
        
        if UIAccessibility.isVoiceOverRunning {
            context += ", VoiceOver: ON"
        }
        
        if UIAccessibility.isSwitchControlRunning {
            context += ", Switch Control: ON"
        }
        
        return context
    }
    
    func announceCurrentElement() {
        let context = captureCurrentContext()
        announceWithVoiceOver(context)
    }
    
    func toggleBookmark() {
        lockQueue.sync {
            let context = captureCurrentContext()
            let bundleId = Bundle.main.bundleIdentifier
            
            if let index = bookmarks.firstIndex(where: { $0.elementDescription == context }) {
                let bookmark = bookmarks[index]
                bookmarks.remove(at: index)
                announceWithVoiceOver("Removed bookmark: \(bookmark.elementDescription)")
                
                if currentBookmarkIndex >= bookmarks.count {
                    currentBookmarkIndex = bookmarks.count > 0 ? 0 : -1
                }
                return
            }
            
            let newBookmark = Bookmark(appBundleIdentifier: bundleId, elementDescription: context)
            bookmarks.append(newBookmark)
            
            announceWithVoiceOver("Added bookmark \(bookmarks.count): \(context)")
        }
    }
    
    func nextBookmark() {
        lockQueue.sync {
            guard !bookmarks.isEmpty else {
                announceWithVoiceOver("No bookmarks saved")
                return
            }
            
            if bookmarks.count == 1 {
                navigateToBookmark(0)
                return
            }
            
            currentBookmarkIndex = (currentBookmarkIndex + 1) % bookmarks.count
            navigateToBookmark(currentBookmarkIndex)
        }
    }
    
    func previousBookmark() {
        lockQueue.sync {
            guard !bookmarks.isEmpty else {
                announceWithVoiceOver("No bookmarks saved")
                return
            }
            
            currentBookmarkIndex = (currentBookmarkIndex - 1 + bookmarks.count) % bookmarks.count
            navigateToBookmark(currentBookmarkIndex)
        }
    }
    
    func listBookmarks() {
        lockQueue.sync {
            guard !bookmarks.isEmpty else {
                announceWithVoiceOver("No bookmarks saved")
                return
            }
            
            var message = "Bookmarks: \(bookmarks.count). "
            for i in 0..<bookmarks.count {
                message += "\(i + 1), \(bookmarks[i].elementDescription). "
            }
            
            announceWithVoiceOver(message)
        }
    }
    
    private func navigateToBookmark(_ index: Int) {
        guard index >= 0 && index < bookmarks.count else { return }
        
        let bookmark = bookmarks[index]
        announceWithVoiceOver("Bookmark \(index + 1): \(bookmark.elementDescription)")
        
        if let bundleId = bookmark.appBundleIdentifier {
            openApp(bundleId: bundleId)
        }
    }
    
    private func openApp(bundleId: String) {
        if let url = URL(string: "\(bundleId)://") {
            if UIApplication.shared.canOpenURL(url) {
                UIApplication.shared.open(url, options: [:], completionHandler: nil)
            }
        }
    }
    
    private func announceWithVoiceOver(_ message: String) {
        UIAccessibility.post(notification: .announcement, argument: message)
        print(message)
    }
}

class SystemNavigatorViewController: UIViewController {
    private let navigator = SystemNavigator()
    private var titleLabel: UILabel!
    private var statusLabel: UILabel!
    private var instructionsLabel: UILabel!
    private var buttonStack: UIStackView!
    private var quickActionsStack: UIStackView!
    
    override func viewDidLoad() {
        super.viewDidLoad()
        setupUI()
        setupGestureRecognizers()
        setupAccessibility()
        checkAccessibilityStatus()
    }
    
    private func setupUI() {
        view.backgroundColor = .systemBackground
        
        titleLabel = UILabel()
        titleLabel.text = "System Navigator"
        titleLabel.font = .systemFont(ofSize: 28, weight: .bold)
        titleLabel.textAlignment = .center
        titleLabel.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(titleLabel)
        
        statusLabel = UILabel()
        statusLabel.text = "Ready"
        statusLabel.font = .systemFont(ofSize: 18)
        statusLabel.textAlignment = .center
        statusLabel.textColor = .systemGreen
        statusLabel.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(statusLabel)
        
        instructionsLabel = UILabel()
        instructionsLabel.text = """
        Navigate iOS with VoiceOver:
        
        • 2-Finger Double Tap: Toggle Bookmark
        • 3-Finger Swipe Right: Next Bookmark
        • 3-Finger Swipe Left: Previous Bookmark
        • 4-Finger Tap: Announce Current
        
        Or use buttons below
        """
        instructionsLabel.font = .systemFont(ofSize: 15)
        instructionsLabel.numberOfLines = 0
        instructionsLabel.textAlignment = .center
        instructionsLabel.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(instructionsLabel)
        
        let scrollView = UIScrollView()
        scrollView.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(scrollView)
        
        buttonStack = UIStackView()
        buttonStack.axis = .vertical
        buttonStack.spacing = 12
        buttonStack.translatesAutoresizingMaskIntoConstraints = false
        scrollView.addSubview(buttonStack)
        
        let bookmarkButtons = [
            ("Toggle Bookmark", #selector(toggleBookmarkTapped), UIColor.systemBlue),
            ("Next Bookmark", #selector(nextBookmarkTapped), UIColor.systemIndigo),
            ("Previous Bookmark", #selector(previousBookmarkTapped), UIColor.systemPurple),
            ("List Bookmarks", #selector(listBookmarksTapped), UIColor.systemTeal),
            ("Announce Current", #selector(announceCurrentTapped), UIColor.systemOrange)
        ]
        
        for (title, action, color) in bookmarkButtons {
            let button = createButton(title: title, action: action, color: color)
            buttonStack.addArrangedSubview(button)
        }
        
        let separator = UIView()
        separator.heightAnchor.constraint(equalToConstant: 20).isActive = true
        buttonStack.addArrangedSubview(separator)
        
        quickActionsStack = UIStackView()
        quickActionsStack.axis = .vertical
        quickActionsStack.spacing = 12
        buttonStack.addArrangedSubview(quickActionsStack)
        
        let quickLabel = UILabel()
        quickLabel.text = "Quick Actions"
        quickLabel.font = .systemFont(ofSize: 20, weight: .semibold)
        quickLabel.textAlignment = .center
        quickActionsStack.addArrangedSubview(quickLabel)
        
        let quickActions = [
            ("Open Settings", #selector(openSettingsTapped)),
            ("Open Control Center", #selector(openControlCenterTapped)),
            ("Open Notification Center", #selector(openNotificationCenterTapped)),
            ("Go Home", #selector(goHomeTapped)),
            ("Open App Switcher", #selector(openAppSwitcherTapped))
        ]
        
        for (title, action) in quickActions {
            let button = createButton(title: title, action: action, color: .systemGray)
            quickActionsStack.addArrangedSubview(button)
        }
        
        NSLayoutConstraint.activate([
            titleLabel.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 20),
            titleLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            titleLabel.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
            
            statusLabel.topAnchor.constraint(equalTo: titleLabel.bottomAnchor, constant: 8),
            statusLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            statusLabel.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
            
            instructionsLabel.topAnchor.constraint(equalTo: statusLabel.bottomAnchor, constant: 20),
            instructionsLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            instructionsLabel.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
            
            scrollView.topAnchor.constraint(equalTo: instructionsLabel.bottomAnchor, constant: 20),
            scrollView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            scrollView.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor),
            
            buttonStack.topAnchor.constraint(equalTo: scrollView.topAnchor),
            buttonStack.leadingAnchor.constraint(equalTo: scrollView.leadingAnchor, constant: 40),
            buttonStack.trailingAnchor.constraint(equalTo: scrollView.trailingAnchor, constant: -40),
            buttonStack.bottomAnchor.constraint(equalTo: scrollView.bottomAnchor, constant: -20),
            buttonStack.widthAnchor.constraint(equalTo: scrollView.widthAnchor, constant: -80)
        ])
    }
    
    private func createButton(title: String, action: Selector, color: UIColor) -> UIButton {
        let button = UIButton(type: .system)
        button.setTitle(title, for: .normal)
        button.titleLabel?.font = .systemFont(ofSize: 17, weight: .medium)
        button.addTarget(self, action: action, for: .touchUpInside)
        button.backgroundColor = color
        button.setTitleColor(.white, for: .normal)
        button.layer.cornerRadius = 12
        button.heightAnchor.constraint(equalToConstant: 50).isActive = true
        return button
    }
    
    private func setupGestureRecognizers() {
        let twoFingerDoubleTap = UITapGestureRecognizer(target: self, action: #selector(toggleBookmarkTapped))
        twoFingerDoubleTap.numberOfTouchesRequired = 2
        twoFingerDoubleTap.numberOfTapsRequired = 2
        view.addGestureRecognizer(twoFingerDoubleTap)
        
        let threeFingerSwipeRight = UISwipeGestureRecognizer(target: self, action: #selector(nextBookmarkTapped))
        threeFingerSwipeRight.direction = .right
        threeFingerSwipeRight.numberOfTouchesRequired = 3
        view.addGestureRecognizer(threeFingerSwipeRight)
        
        let threeFingerSwipeLeft = UISwipeGestureRecognizer(target: self, action: #selector(previousBookmarkTapped))
        threeFingerSwipeLeft.direction = .left
        threeFingerSwipeLeft.numberOfTouchesRequired = 3
        view.addGestureRecognizer(threeFingerSwipeLeft)
        
        let fourFingerTap = UITapGestureRecognizer(target: self, action: #selector(announceCurrentTapped))
        fourFingerTap.numberOfTouchesRequired = 4
        view.addGestureRecognizer(fourFingerTap)
    }
    
    private func setupAccessibility() {
        view.isAccessibilityElement = false
        titleLabel.isAccessibilityElement = true
        statusLabel.isAccessibilityElement = true
        instructionsLabel.isAccessibilityElement = true
        
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(voiceOverStatusChanged),
            name: UIAccessibility.voiceOverStatusDidChangeNotification,
            object: nil
        )
        
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(switchControlStatusChanged),
            name: UIAccessibility.switchControlStatusDidChangeNotification,
            object: nil
        )
    }
    
    private func checkAccessibilityStatus() {
        var status = "Ready"
        
        if UIAccessibility.isVoiceOverRunning {
            status = "VoiceOver Active"
        } else if UIAccessibility.isSwitchControlRunning {
            status = "Switch Control Active"
        } else {
            status = "No Assistive Tech Detected"
        }
        
        statusLabel.text = status
        updateStatusColor()
    }
    
    private func updateStatusColor() {
        if UIAccessibility.isVoiceOverRunning || UIAccessibility.isSwitchControlRunning {
            statusLabel.textColor = .systemGreen
        } else {
            statusLabel.textColor = .systemOrange
        }
    }
    
    @objc private func voiceOverStatusChanged() {
        checkAccessibilityStatus()
        if UIAccessibility.isVoiceOverRunning {
            UIAccessibility.post(notification: .announcement, argument: "Voice Over enabled. System Navigator ready.")
        }
    }
    
    @objc private func switchControlStatusChanged() {
        checkAccessibilityStatus()
        if UIAccessibility.isSwitchControlRunning {
            UIAccessibility.post(notification: .announcement, argument: "Switch Control enabled. System Navigator ready.")
        }
    }
    
    @objc private func toggleBookmarkTapped() {
        navigator.toggleBookmark()
        provideHapticFeedback()
    }
    
    @objc private func nextBookmarkTapped() {
        navigator.nextBookmark()
        provideHapticFeedback()
    }
    
    @objc private func previousBookmarkTapped() {
        navigator.previousBookmark()
        provideHapticFeedback()
    }
    
    @objc private func listBookmarksTapped() {
        navigator.listBookmarks()
    }
    
    @objc private func announceCurrentTapped() {
        navigator.announceCurrentElement()
    }
    
    @objc private func openSettingsTapped() {
        if let url = URL(string: UIApplication.openSettingsURLString) {
            UIApplication.shared.open(url)
        }
    }
    
    @objc private func openControlCenterTapped() {
        UIAccessibility.post(notification: .announcement, argument: "Swipe down from top-right corner to open Control Center")
    }
    
    @objc private func openNotificationCenterTapped() {
        UIAccessibility.post(notification: .announcement, argument: "Swipe down from top-left corner to open Notification Center")
    }
    
    @objc private func goHomeTapped() {
        UIAccessibility.post(notification: .announcement, argument: "Swipe up from bottom to go home, or press home button")
    }
    
    @objc private func openAppSwitcherTapped() {
        UIAccessibility.post(notification: .announcement, argument: "Swipe up and hold to open app switcher")
    }
    
    private func provideHapticFeedback() {
        let generator = UINotificationFeedbackGenerator()
        generator.notificationOccurred(.success)
    }
}

class SiriShortcutManager {
    static func registerShortcuts() {
        UIAccessibility.post(notification: .announcement, argument: "System Navigator shortcuts available via Shortcuts app")
    }
}

@UIApplicationMain
class AppDelegate: UIResponder, UIApplicationDelegate {
    var window: UIWindow?
    
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        window = UIWindow(frame: UIScreen.main.bounds)
        
        let navController = UINavigationController(rootViewController: SystemNavigatorViewController())
        navController.navigationBar.prefersLargeTitles = true
        
        window?.rootViewController = navController
        window?.makeKeyAndVisible()
        
        SiriShortcutManager.registerShortcuts()
        
        return true
    }
    
    func application(_ application: UIApplication, continue userActivity: NSUserActivity, restorationHandler: @escaping ([UIUserActivityRestoring]?) -> Void) -> Bool {
        if userActivity.activityType == NSUserActivityTypeBrowsingWeb {
            return true
        }
        return false
    }
}
