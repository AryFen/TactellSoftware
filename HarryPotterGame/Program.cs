using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BrailleDisplay.SDK;

namespace BrailleDisplay.Demo
{
    /// <summary>
    /// "The Forbidden Forest Mystery" - A Harry Potter Choose-Your-Own-Adventure
    /// 
    /// You're a Hogwarts student who discovers something strange in the Forbidden Forest.
    /// Make choices, solve mysteries, and see where your adventure leads!
    /// 
    /// Features demonstrated:
    /// - Hierarchical story navigation
    /// - Choice-based gameplay
    /// - Bookmarking key story moments
    /// - Multiple endings based on decisions
    /// - Inventory system
    /// </summary>
    public class HarryPotterAdventure : IDisposable
    {
        private BrailleDisplayDevice _display;
        private StoryEngine _story;
        private GameState _gameState;

        // Game state tracking
        private class GameState
        {
            public StoryNode CurrentNode { get; set; }
            public int SelectedChoiceIndex { get; set; } = 0;
            public List<string> Inventory { get; set; } = new List<string>();
            public Dictionary<string, bool> Flags { get; set; } = new Dictionary<string, bool>();
            public int CouragePoints { get; set; } = 0;
            public int WisdomPoints { get; set; } = 0;
            public int FriendshipPoints { get; set; } = 0;
            public string HouseName { get; set; } = "Gryffindor";
        }

        // Story node structure
        private class StoryNode
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Text { get; set; }
            public List<Choice> Choices { get; set; } = new List<Choice>();
            public bool IsEnding { get; set; } = false;
            public Action<GameState> OnEnter { get; set; }
            public string ItemFound { get; set; }
        }

        private class Choice
        {
            public string Text { get; set; }
            public string NextNodeId { get; set; }
            public Func<GameState, bool> IsAvailable { get; set; } = (state) => true;
            public Action<GameState> OnSelect { get; set; }
            public string RequirementText { get; set; }
        }

        private class StoryEngine
        {
            private Dictionary<string, StoryNode> _nodes = new Dictionary<string, StoryNode>();

            public StoryEngine()
            {
                BuildStory();
            }

            public StoryNode GetNode(string id) => _nodes.ContainsKey(id) ? _nodes[id] : null;

            private void BuildStory()
            {
                // ========== OPENING ==========
                _nodes["start"] = new StoryNode
                {
                    Id = "start",
                    Title = "A Strange Discovery",
                    Text = "You're walking back from Herbology when you notice strange blue light coming from the Forbidden Forest. Professor McGonagall is nowhere in sight. What do you do?",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Investigate the light alone",
                            NextNodeId = "forest_alone",
                            OnSelect = (s) => s.CouragePoints += 2
                        },
                        new Choice
                        {
                            Text = "Find Hermione for help",
                            NextNodeId = "get_hermione",
                            OnSelect = (s) => s.WisdomPoints += 2
                        },
                        new Choice
                        {
                            Text = "Report to a teacher",
                            NextNodeId = "tell_teacher",
                            OnSelect = (s) => s.WisdomPoints += 1
                        },
                        new Choice
                        {
                            Text = "Ignore it and go to dinner",
                            NextNodeId = "ignore_ending"
                        }
                    }
                };

                // ========== BRAVE PATH ==========
                _nodes["forest_alone"] = new StoryNode
                {
                    Id = "forest_alone",
                    Title = "Into the Darkness",
                    Text = "You venture into the forest. The blue light grows brighter. You hear a low growl. Behind a tree, you spot a wounded unicorn! But something else is approaching...",
                    ItemFound = "Wand",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Cast Lumos to see better",
                            NextNodeId = "cast_lumos",
                            OnSelect = (s) => s.CouragePoints += 1
                        },
                        new Choice
                        {
                            Text = "Hide behind the tree",
                            NextNodeId = "hide_tree"
                        },
                        new Choice
                        {
                            Text = "Run back to the castle",
                            NextNodeId = "run_away"
                        }
                    }
                };

                _nodes["cast_lumos"] = new StoryNode
                {
                    Id = "cast_lumos",
                    Title = "The Reveal",
                    Text = "Your wand lights up the clearing. A cloaked figure is drinking the unicorn's blood! It's a dark wizard! Suddenly, centaurs burst through the trees, led by Firenze. 'Leave, student!' he commands.",
                    ItemFound = "Silver unicorn hair",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Thank Firenze and leave quickly",
                            NextNodeId = "firenze_save"
                        },
                        new Choice
                        {
                            Text = "Ask Firenze about the wizard",
                            NextNodeId = "ask_firenze",
                            OnSelect = (s) => s.WisdomPoints += 2
                        },
                        new Choice
                        {
                            Text = "Try to help the unicorn",
                            NextNodeId = "help_unicorn",
                            OnSelect = (s) => s.CouragePoints += 3
                        }
                    }
                };

                // ========== HERMIONE PATH ==========
                _nodes["get_hermione"] = new StoryNode
                {
                    Id = "get_hermione",
                    Title = "Hermione's Plan",
                    Text = "You find Hermione in the library. 'Blue light? That's unusual. We should investigate together, but carefully. I'll bring some useful potions.' She grabs her bag.",
                    ItemFound = "Hermione's potion bag",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Go to the forest together",
                            NextNodeId = "forest_with_hermione",
                            OnSelect = (s) => s.FriendshipPoints += 2
                        },
                        new Choice
                        {
                            Text = "Research in the library first",
                            NextNodeId = "research_first",
                            OnSelect = (s) => s.WisdomPoints += 3
                        },
                        new Choice
                        {
                            Text = "Get Ron and Harry too",
                            NextNodeId = "get_trio",
                            OnSelect = (s) => s.FriendshipPoints += 3
                        }
                    }
                };

                _nodes["research_first"] = new StoryNode
                {
                    Id = "research_first",
                    Title = "Ancient Knowledge",
                    Text = "Hermione finds a book: 'Magical Creatures and Their Distress Signals'. 'Blue light means a magical creature is in mortal danger!' You now know what you're facing.",
                    ItemFound = "Knowledge of creature signals",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Rush to the forest to help",
                            NextNodeId = "forest_with_hermione"
                        },
                        new Choice
                        {
                            Text = "Tell Professor McGonagall",
                            NextNodeId = "tell_mcgonagall",
                            OnSelect = (s) => s.WisdomPoints += 2
                        }
                    }
                };

                _nodes["forest_with_hermione"] = new StoryNode
                {
                    Id = "forest_with_hermione",
                    Title = "The Discovery",
                    Text = "You and Hermione find the unicorn. A dark figure looms over it. 'Use the distraction potion!' Hermione whispers, handing you a vial. The figure turns...",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Throw the potion",
                            NextNodeId = "potion_success",
                            OnSelect = (s) => s.CouragePoints += 2
                        },
                        new Choice
                        {
                            Text = "Cast Expelliarmus together",
                            NextNodeId = "dual_cast",
                            OnSelect = (s) => { s.CouragePoints += 3; s.FriendshipPoints += 2; }
                        },
                        new Choice
                        {
                            Text = "Call for help loudly",
                            NextNodeId = "call_help"
                        }
                    }
                };

                // ========== ENDINGS ==========
                _nodes["firenze_save"] = new StoryNode
                {
                    Id = "firenze_save",
                    Title = "The Brave Hero",
                    Text = "You return to the castle and tell Dumbledore everything. 'Your courage tonight saved that unicorn's life,' he says. '50 points to your house!' You're a hero!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["hero_ending"] = true
                };

                _nodes["potion_success"] = new StoryNode
                {
                    Id = "potion_success",
                    Title = "Clever Victory",
                    Text = "The potion creates a blinding flash! The dark figure flees. Firenze appears: 'You and your friend showed great wisdom and bravery.' The unicorn is saved. Dumbledore awards you both 75 points!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["best_ending"] = true
                };

                _nodes["dual_cast"] = new StoryNode
                {
                    Id = "dual_cast",
                    Title = "Together We Stand",
                    Text = "Your spells combined knock the wizard back! The centaurs arrive and chase him away. 'The power of friendship saved the day,' says Dumbledore later. You and Hermione are celebrated throughout Hogwarts!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["friendship_ending"] = true
                };

                _nodes["tell_mcgonagall"] = new StoryNode
                {
                    Id = "tell_mcgonagall",
                    Title = "The Wise Choice",
                    Text = "McGonagall immediately alerts Dumbledore. They rescue the unicorn. 'You showed excellent judgment,' McGonagall says. 'Not all heroism is about rushing into danger.' 60 points to your house!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["wisdom_ending"] = true
                };

                _nodes["ignore_ending"] = new StoryNode
                {
                    Id = "ignore_ending",
                    Title = "A Missed Adventure",
                    Text = "You go to dinner. Later you hear a unicorn was killed in the forest. 'If only someone had investigated...' Hermione sighs. You wonder what might have been different.",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["bad_ending"] = true
                };

                _nodes["hide_tree"] = new StoryNode
                {
                    Id = "hide_tree",
                    Title = "Hidden Observer",
                    Text = "You watch as the dark figure feeds on the unicorn. Centaurs arrive and chase it away. Firenze spots you: 'You were wise to stay hidden, young one. But speak of this to Dumbledore.'",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["cautious_ending"] = true
                };

                _nodes["run_away"] = new StoryNode
                {
                    Id = "run_away",
                    Title = "Safe Return",
                    Text = "You run back to the castle and immediately find McGonagall. She sends Hagrid to investigate. 'You did the right thing,' she says. 'Sometimes the bravest choice is getting help.' 40 points!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["safe_ending"] = true
                };

                _nodes["ask_firenze"] = new StoryNode
                {
                    Id = "ask_firenze",
                    Title = "The Prophecy",
                    Text = "Firenze looks at you gravely. 'Dark times are coming. But you have shown courage. The stars suggest you will play a part in what's to come.' You return to tell Dumbledore, who listens intently. 65 points!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["prophecy_ending"] = true
                };

                _nodes["help_unicorn"] = new StoryNode
                {
                    Id = "help_unicorn",
                    Title = "The Healer's Heart",
                    Text = "You approach the wounded unicorn despite the danger. Its horn glows and touches your hand - a mark of pure magic! The centaurs are amazed. 'The unicorn has blessed you,' Firenze says. You're now connected to forest magic. 80 points!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["blessed_ending"] = true
                };

                _nodes["get_trio"] = new StoryNode
                {
                    Id = "get_trio",
                    Title = "The Dream Team",
                    Text = "With Harry, Ron, and Hermione by your side, you approach the forest. Harry's scar burns - dark magic ahead! Ron suggests using his brother's two-way mirror to call for backup while you investigate.",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Text = "Accept Ron's backup plan",
                            NextNodeId = "smart_backup",
                            OnSelect = (s) => s.WisdomPoints += 3
                        },
                        new Choice
                        {
                            Text = "Go in as a group immediately",
                            NextNodeId = "group_forest",
                            OnSelect = (s) => s.CouragePoints += 2
                        }
                    }
                };

                _nodes["smart_backup"] = new StoryNode
                {
                    Id = "smart_backup",
                    Title = "Perfect Strategy",
                    Text = "Your group finds the unicorn just as the dark wizard appears. But with the mirror, you've already alerted Dumbledore! He arrives instantly with McGonagall. The wizard is captured! 'Exemplary teamwork,' says Dumbledore. 100 points to your house!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["perfect_ending"] = true
                };

                _nodes["group_forest"] = new StoryNode
                {
                    Id = "group_forest",
                    Title = "United We Stand",
                    Text = "The four of you face the dark wizard together. With combined spells (Expelliarmus, Petrificus Totalus, and Lumos Maxima), you drive him away! The unicorn is saved. Dumbledore awards each of you 70 points. Gryffindor wins the House Cup!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["unity_ending"] = true
                };

                _nodes["tell_teacher"] = new StoryNode
                {
                    Id = "tell_teacher",
                    Title = "The Responsible Choice",
                    Text = "You find Professor Sprout and tell her about the light. She immediately alerts Dumbledore. They investigate and save the unicorn. 'You did exactly right,' Dumbledore says warmly. 50 points for responsibility!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["responsible_ending"] = true
                };

                _nodes["call_help"] = new StoryNode
                {
                    Id = "call_help",
                    Title = "Timely Rescue",
                    Text = "Your shouts alert Hagrid, who was nearby. He charges in with Fang, scaring the dark figure away. 'Yeh did good,' Hagrid says. 'Knew when yeh needed help.' 55 points to your house!",
                    IsEnding = true,
                    OnEnter = (s) => s.Flags["rescue_ending"] = true
                };
            }
        }

        public HarryPotterAdventure(string portName = "COM3")
        {
            _display = new BrailleDisplayDevice(4, 40, "HarryPotterAdventure", portName);
            _story = new StoryEngine();
            _gameState = new GameState();

            SetupButtonHandlers();
        }

        private void SetupButtonHandlers()
        {
            // Next Element - Move down through choices
            _display.SetNextElementHandler(async () =>
            {
                if (!_gameState.CurrentNode.IsEnding)
                {
                    var availableChoices = GetAvailableChoices();
                    if (availableChoices.Count > 0)
                    {
                        _gameState.SelectedChoiceIndex =
                            (_gameState.SelectedChoiceIndex + 1) % availableChoices.Count;
                        ShowCurrentScene();
                    }
                }
                await Task.CompletedTask;
            });

            // Previous Element - Move up through choices
            _display.SetPreviousElementHandler(async () =>
            {
                if (!_gameState.CurrentNode.IsEnding)
                {
                    var availableChoices = GetAvailableChoices();
                    if (availableChoices.Count > 0)
                    {
                        _gameState.SelectedChoiceIndex--;
                        if (_gameState.SelectedChoiceIndex < 0)
                            _gameState.SelectedChoiceIndex = availableChoices.Count - 1;
                        ShowCurrentScene();
                    }
                }
                await Task.CompletedTask;
            });

            // Select - Make choice and advance story
            _display.SetSelectHandler(async () =>
            {
                if (_gameState.CurrentNode.IsEnding)
                {
                    // Restart game
                    ShowStatus("Restarting adventure...");
                    await Task.Delay(1500);
                    StartGame();
                }
                else
                {
                    var availableChoices = GetAvailableChoices();
                    if (availableChoices.Count > 0 &&
                        _gameState.SelectedChoiceIndex < availableChoices.Count)
                    {
                        var choice = availableChoices[_gameState.SelectedChoiceIndex];
                        MakeChoice(choice);
                    }
                }
                await Task.CompletedTask;
            });

            // Back - Show story recap / can't go back in choices
            _display.SetBackHandler(async () =>
            {
                ShowStoryRecap();
                await Task.Delay(3000);
                ShowCurrentScene();
            });

            // Toggle Bookmark - Save current story moment
            _display.SetToggleBookmarkHandler(async () =>
            {
                string location = _gameState.CurrentNode.Id;
                string displayText = _gameState.CurrentNode.Title;

                bool added = _display.Bookmarks.ToggleBookmark(location, displayText);

                ShowStatus(added ? "Moment saved!" : "Moment forgotten");
                await Task.Delay(1500);
                ShowCurrentScene();
            });

            // Next Bookmark - Jump to saved story moment (for reviewing)
            _display.SetNextBookmarkHandler(async () =>
            {
                var bookmark = _display.Bookmarks.NextBookmark();
                if (bookmark != null)
                {
                    ShowStatus($"Memory: {bookmark.DisplayText}");
                    await Task.Delay(2500);
                    ShowCurrentScene();
                }
                else
                {
                    ShowStatus("No saved moments");
                    await Task.Delay(1500);
                    ShowCurrentScene();
                }
            });
        }

        private List<Choice> GetAvailableChoices()
        {
            return _gameState.CurrentNode.Choices
                .Where(c => c.IsAvailable(_gameState))
                .ToList();
        }

        private void MakeChoice(Choice choice)
        {
            // Execute choice effects
            choice.OnSelect?.Invoke(_gameState);

            // Transition animation
            ShowStatus("Choosing...");
            System.Threading.Thread.Sleep(800);

            // Load next node
            var nextNode = _story.GetNode(choice.NextNodeId);
            if (nextNode != null)
            {
                _gameState.CurrentNode = nextNode;
                _gameState.SelectedChoiceIndex = 0;

                // Execute node entry effects
                nextNode.OnEnter?.Invoke(_gameState);

                // Add item if found
                if (!string.IsNullOrEmpty(nextNode.ItemFound))
                {
                    _gameState.Inventory.Add(nextNode.ItemFound);
                    ShowStatus($"Found: {nextNode.ItemFound}!");
                    System.Threading.Thread.Sleep(1500);
                }

                // Navigate in display history
                _display.NavigateTo(nextNode.Id);

                ShowCurrentScene();
            }
        }

        private void ShowCurrentScene()
        {
            _display.Clear();

            var node = _gameState.CurrentNode;

            // Row 0: Title
            string title = node.Title;
            if (title.Length > _display.Columns)
                title = title.Substring(0, _display.Columns - 3) + "...";
            _display.DisplayText(title, 0, 0);

            // Row 1: Separator or "THE END"
            if (node.IsEnding)
            {
                _display.DisplayText("======= THE END =======", 1, 0);

                // Row 2-3: Show stats
                _display.DisplayText($"Courage:{_gameState.CouragePoints} Wisdom:{_gameState.WisdomPoints}", 2, 0);
                _display.DisplayText("(Select to restart)", 3, 0);

                LogToConsole($"ENDING: {node.Title}");
                LogToConsole($"Stats - Courage:{_gameState.CouragePoints} Wisdom:{_gameState.WisdomPoints} Friendship:{_gameState.FriendshipPoints}");
            }
            else
            {
                _display.DisplayText("---", 1, 0);

                // Row 2-3: Current choice
                var availableChoices = GetAvailableChoices();
                if (availableChoices.Count > 0 && _gameState.SelectedChoiceIndex < availableChoices.Count)
                {
                    var choice = availableChoices[_gameState.SelectedChoiceIndex];
                    string choiceText = $">{choice.Text}";
                    if (choiceText.Length > _display.Columns)
                        choiceText = choiceText.Substring(0, _display.Columns - 3) + "...";
                    _display.DisplayText(choiceText, 2, 0);

                    // Show next choice as preview
                    if (_gameState.SelectedChoiceIndex + 1 < availableChoices.Count)
                    {
                        var nextChoice = availableChoices[_gameState.SelectedChoiceIndex + 1];
                        string nextText = $" {nextChoice.Text}";
                        if (nextText.Length > _display.Columns)
                            nextText = nextText.Substring(0, _display.Columns - 3) + "...";
                        _display.DisplayText(nextText, 3, 0);
                    }
                }
            }

            LogToConsole($"Scene: {node.Title}");
            LogToConsole($"  {node.Text}");
        }

        private void ShowStatus(string message)
        {
            _display.Clear();
            _display.DisplayText(message, 1, 0);
            LogToConsole($"Status: {message}");
        }

        private void ShowStoryRecap()
        {
            _display.Clear();
            _display.DisplayText("Your Journey:", 0, 0);
            _display.DisplayText($"Items: {_gameState.Inventory.Count}", 1, 0);
            _display.DisplayText($"C:{_gameState.CouragePoints} W:{_gameState.WisdomPoints} F:{_gameState.FriendshipPoints}", 2, 0);

            LogToConsole("=== Story Recap ===");
            LogToConsole($"Inventory: {string.Join(", ", _gameState.Inventory)}");
            LogToConsole($"Courage: {_gameState.CouragePoints}, Wisdom: {_gameState.WisdomPoints}, Friendship: {_gameState.FriendshipPoints}");
        }

        private void LogToConsole(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void StartGame()
        {
            _gameState = new GameState();
            _gameState.CurrentNode = _story.GetNode("start");
            _display.ClearNavigationHistory();

            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     THE FORBIDDEN FOREST MYSTERY - A HP Adventure          ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("📖 Your choices shape the story");
            Console.WriteLine("🎯 Earn Courage, Wisdom, and Friendship points");
            Console.WriteLine("🔖 Bookmark memorable moments");
            Console.WriteLine("🏆 Discover multiple endings!");
            Console.WriteLine();
            Console.WriteLine("Button Guide:");
            Console.WriteLine("  Next/Previous Element : Navigate choices");
            Console.WriteLine("  Select                : Make your choice");
            Console.WriteLine("  Back                  : View your stats");
            Console.WriteLine("  Toggle Bookmark       : Save this moment");
            Console.WriteLine("  Next Bookmark         : Review saved moments");
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("Adventure Log:");
            Console.WriteLine("─────────────");

            ShowCurrentScene();
        }

        public void Run()
        {
            StartGame();

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("KEYBOARD CONTROLS (for testing without hardware):");
            Console.WriteLine("  N - Next Element");
            Console.WriteLine("  P - Previous Element");
            Console.WriteLine("  S or Enter - Select");
            Console.WriteLine("  B or Backspace - Back");
            Console.WriteLine("  T - Toggle Bookmark");
            Console.WriteLine("  M - Next Bookmark (jump to saved moment)");
            Console.WriteLine("  ESC - Exit game");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Escape)
                        break;

                    // Simulate button presses
                    SimulateButtonFromKeyboard(key.Key);
                }

                System.Threading.Thread.Sleep(50);
            }
        }

        private async void SimulateButtonFromKeyboard(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.N: // Next Element
                    Console.WriteLine("[KEYBOARD] Next Element");
                    if (!_gameState.CurrentNode.IsEnding)
                    {
                        var availableChoices = GetAvailableChoices();
                        if (availableChoices.Count > 0)
                        {
                            _gameState.SelectedChoiceIndex =
                                (_gameState.SelectedChoiceIndex + 1) % availableChoices.Count;
                            ShowCurrentScene();
                        }
                    }
                    break;

                case ConsoleKey.P: // Previous Element
                    Console.WriteLine("[KEYBOARD] Previous Element");
                    if (!_gameState.CurrentNode.IsEnding)
                    {
                        var availableChoices = GetAvailableChoices();
                        if (availableChoices.Count > 0)
                        {
                            _gameState.SelectedChoiceIndex--;
                            if (_gameState.SelectedChoiceIndex < 0)
                                _gameState.SelectedChoiceIndex = availableChoices.Count - 1;
                            ShowCurrentScene();
                        }
                    }
                    break;

                case ConsoleKey.S:
                case ConsoleKey.Enter: // Select
                    Console.WriteLine("[KEYBOARD] Select");
                    if (_gameState.CurrentNode.IsEnding)
                    {
                        ShowStatus("Restarting adventure...");
                        await Task.Delay(1500);
                        StartGame();
                    }
                    else
                    {
                        var availableChoices = GetAvailableChoices();
                        if (availableChoices.Count > 0 &&
                            _gameState.SelectedChoiceIndex < availableChoices.Count)
                        {
                            var choice = availableChoices[_gameState.SelectedChoiceIndex];
                            MakeChoice(choice);
                        }
                    }
                    break;

                case ConsoleKey.B:
                case ConsoleKey.Backspace: // Back
                    Console.WriteLine("[KEYBOARD] Back");
                    ShowStoryRecap();
                    await Task.Delay(3000);
                    ShowCurrentScene();
                    break;

                case ConsoleKey.T: // Toggle Bookmark
                    Console.WriteLine("[KEYBOARD] Toggle Bookmark");
                    if (_gameState.CurrentNode != null)
                    {
                        string location = _gameState.CurrentNode.Id;
                        string displayText = _gameState.CurrentNode.Title;

                        bool added = _display.Bookmarks.ToggleBookmark(location, displayText);

                        ShowStatus(added ? "Moment saved!" : "Moment forgotten");
                        await Task.Delay(1500);
                        ShowCurrentScene();
                    }
                    break;

                case ConsoleKey.M: // Next Bookmark
                    Console.WriteLine("[KEYBOARD] Next Bookmark");
                    var bookmark = _display.Bookmarks.NextBookmark();
                    if (bookmark != null)
                    {
                        ShowStatus($"Memory: {bookmark.DisplayText}");
                        await Task.Delay(2500);
                        ShowCurrentScene();
                    }
                    else
                    {
                        ShowStatus("No saved moments");
                        await Task.Delay(1500);
                        ShowCurrentScene();
                    }
                    break;
            }
        }

        public void Dispose()
        {
            _display?.Dispose();
        }
    }

    // Main Program Entry Point
    class Program
    {
        static void Main(string[] args)
        {
            string port = "COM3";

            if (args.Length > 0)
            {
                port = args[0];
            }

            try
            {
                using var game = new HarryPotterAdventure(port);
                game.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine("\nMake sure your braille display is connected!");
                Console.WriteLine($"Usage: HarryPotterGame.exe {port}");
            }

            Console.WriteLine("\nThanks for playing! Press any key to exit...");
            Console.ReadKey();
        }
    }
}