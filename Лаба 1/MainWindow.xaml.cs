using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TextEditor
{
    public partial class MainWindow : Window
    {
        private string currentFilePath = null;
        private bool isTextChanged = false;
        private Lab6Lexer lexer;
        private Lab6Parser parser;

        public MainWindow()
        {
            InitializeComponent();
            lexer = new Lab6Lexer();
            parser = new Lab6Parser();
            InitializeNewDocument();
        }

        // =============================================
        // МОДЕЛИ ДАННЫХ
        // =============================================

        public class Token
        {
            public string Type { get; set; }
            public string Value { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
        }

        public class Tetrad
        {
            public int Index { get; set; }
            public string Op { get; set; }
            public string Arg1 { get; set; }
            public string Arg2 { get; set; }
            public string Result { get; set; }
        }

        public class AnalysisError
        {
            public string ErrorType { get; set; }
            public string Message { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
        }

        // =============================================
        // ЛЕКСИЧЕСКИЙ АНАЛИЗАТОР
        // =============================================

        public class Lab6Lexer
        {
            public (List<Token> tokens, List<AnalysisError> errors) Tokenize(string input)
            {
                var tokens = new List<Token>();
                var errors = new List<AnalysisError>();

                if (string.IsNullOrEmpty(input))
                {
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Ошибка ввода",
                        Message = "Пустая строка. Введите арифметическое выражение.",
                        Line = 1,
                        Position = 1
                    });
                    return (tokens, errors);
                }

                int position = 0;
                int line = 1;
                int lineStart = 0;

                while (position < input.Length)
                {
                    char c = input[position];

                    // Пропуск пробельных символов
                    if (char.IsWhiteSpace(c))
                    {
                        if (c == '\n')
                        {
                            line++;
                            lineStart = position + 1;
                        }
                        position++;
                        continue;
                    }

                    int col = position - lineStart + 1;

                    // Скобки
                    if (c == '(')
                    {
                        tokens.Add(new Token { Type = "LPAREN", Value = "(", Line = line, Position = col });
                        position++;
                        continue;
                    }
                    if (c == ')')
                    {
                        tokens.Add(new Token { Type = "RPAREN", Value = ")", Line = line, Position = col });
                        position++;
                        continue;
                    }

                    // Составные операторы ** и //
                    if (c == '*' && position + 1 < input.Length && input[position + 1] == '*')
                    {
                        tokens.Add(new Token { Type = "OPERATOR", Value = "**", Line = line, Position = col });
                        position += 2;
                        continue;
                    }
                    if (c == '/' && position + 1 < input.Length && input[position + 1] == '/')
                    {
                        tokens.Add(new Token { Type = "OPERATOR", Value = "//", Line = line, Position = col });
                        position += 2;
                        continue;
                    }

                    // Одиночные операторы
                    if (c == '+' || c == '-' || c == '*' || c == '/' || c == '%')
                    {
                        tokens.Add(new Token { Type = "OPERATOR", Value = c.ToString(), Line = line, Position = col });
                        position++;
                        continue;
                    }

                    // Числа
                    if (char.IsDigit(c))
                    {
                        int start = position;
                        while (position < input.Length && char.IsDigit(input[position]))
                            position++;
                        string number = input.Substring(start, position - start);
                        tokens.Add(new Token { Type = "NUMBER", Value = number, Line = line, Position = col });
                        continue;
                    }

                    // Идентификаторы (начинаются с буквы или _)
                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = position;
                        while (position < input.Length &&
                               (char.IsLetterOrDigit(input[position]) || input[position] == '_'))
                            position++;
                        string id = input.Substring(start, position - start);
                        tokens.Add(new Token { Type = "IDENTIFIER", Value = id, Line = line, Position = col });
                        continue;
                    }

                    // Недопустимый символ
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Лексическая ошибка",
                        Message = $"Недопустимый символ '{c}' (код {(int)c})",
                        Line = line,
                        Position = col
                    });
                    position++;
                }

                return (tokens, errors);
            }
        }

        // =============================================
        // СИНТАКСИЧЕСКИЙ АНАЛИЗАТОР (Рекурсивный спуск)
        // =============================================

        public class Lab6Parser
        {
            private List<Token> tokens;
            private int position;
            private List<AnalysisError> errors;
            private List<Tetrad> tetrads;
            private int tempCounter;
            private List<string> poliz;
            private bool hasErrors;
            private bool allIntegers; // все операнды — целые числа

            public (List<Tetrad> tetrads, List<string> poliz, List<AnalysisError> errors, int? result)
                Parse(List<Token> tokens)
            {
                this.tokens = tokens ?? new List<Token>();
                position = 0;
                errors = new List<AnalysisError>();
                tetrads = new List<Tetrad>();
                tempCounter = 0;
                poliz = new List<string>();
                hasErrors = false;
                allIntegers = true;

                if (this.tokens.Count == 0)
                {
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Ошибка ввода",
                        Message = "Нет лексем для анализа.",
                        Line = 1,
                        Position = 1
                    });
                    hasErrors = true;
                    return (tetrads, poliz, errors, null);
                }

                // Проверяем, все ли операнды — NUMBER (не IDENTIFIER)
                foreach (var t in this.tokens)
                {
                    if (t.Type == "IDENTIFIER")
                    {
                        allIntegers = false;
                        break;
                    }
                }

                // Запуск разбора
                ParseE();

                // Лишние токены после разбора
                if (!hasErrors && position < this.tokens.Count)
                {
                    var tok = this.tokens[position];
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Синтаксическая ошибка",
                        Message = $"Неожиданный токен '{tok.Value}' после завершения выражения.",
                        Line = tok.Line,
                        Position = tok.Position
                    });
                    hasErrors = true;
                }

                // При ошибках — очищаем всё
                if (hasErrors)
                {
                    poliz.Clear();
                    tetrads.Clear();
                    return (tetrads, poliz, errors, null);
                }

                // ПОЛИЗ — только если все операнды целые числа
                if (!allIntegers)
                {
                    poliz.Clear();
                    return (tetrads, new List<string>(), errors, null);
                }

                // Вычисление
                int? evalResult = EvaluatePoliz();
                return (tetrads, poliz, errors, evalResult);
            }

            private string NewTemp()
            {
                tempCounter++;
                return $"t{tempCounter}";
            }

            private void AddTetrad(string op, string arg1, string arg2, string result)
            {
                tetrads.Add(new Tetrad
                {
                    Index = tetrads.Count + 1,
                    Op = op,
                    Arg1 = arg1,
                    Arg2 = arg2,
                    Result = result
                });
            }

            private Token Current()
            {
                return position < tokens.Count ? tokens[position] : null;
            }

            private void Expect(string expectedType)
            {
                var tok = Current();
                if (tok != null && tok.Type == expectedType)
                {
                    position++;
                }
                else
                {
                    string found = tok != null ? $"'{tok.Value}'" : "конец строки";
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Синтаксическая ошибка",
                        Message = $"Ожидалась '{expectedType}', найдено: {found}",
                        Line = tok?.Line ?? 1,
                        Position = tok?.Position ?? 1
                    });
                    hasErrors = true;
                }
            }

            // E → T A
            private string ParseE()
            {
                string t = ParseT();
                return ParseA(t);
            }

            // A → ε | + T A | - T A
            private string ParseA(string inherited)
            {
                var tok = Current();
                if (tok != null && tok.Type == "OPERATOR" &&
                    (tok.Value == "+" || tok.Value == "-"))
                {
                    string op = tok.Value;
                    position++;
                    string t = ParseT();
                    string temp = NewTemp();
                    AddTetrad(op, inherited, t, temp);

                    if (allIntegers)
                    {
                        poliz.Add(op);
                    }

                    return ParseA(temp);
                }
                return inherited;
            }

            // T → F B
            private string ParseT()
            {
                string f = ParseF();
                return ParseB(f);
            }

            // B → ε | * F B | / F B | // F B | % F B | ** F B
            private string ParseB(string inherited)
            {
                var tok = Current();
                if (tok != null && tok.Type == "OPERATOR" &&
                    (tok.Value == "*" || tok.Value == "/" || tok.Value == "//" ||
                     tok.Value == "%" || tok.Value == "**"))
                {
                    string op = tok.Value;
                    position++;
                    string f = ParseF();
                    string temp = NewTemp();
                    AddTetrad(op, inherited, f, temp);

                    if (allIntegers)
                    {
                        poliz.Add(op);
                    }

                    return ParseB(temp);
                }
                return inherited;
            }

            // F → num | id | ( E )
            private string ParseF()
            {
                var tok = Current();
                if (tok == null)
                {
                    errors.Add(new AnalysisError
                    {
                        ErrorType = "Синтаксическая ошибка",
                        Message = "Ожидался операнд (число, идентификатор или скобка), найдено: конец строки",
                        Line = 1,
                        Position = 1
                    });
                    hasErrors = true;
                    return "error";
                }

                if (tok.Type == "NUMBER" || tok.Type == "IDENTIFIER")
                {
                    position++;
                    if (allIntegers)
                        poliz.Add(tok.Value);
                    return tok.Value;
                }

                if (tok.Type == "LPAREN")
                {
                    position++;
                    string e = ParseE();
                    if (!hasErrors)
                        Expect("RPAREN");
                    return e;
                }

                errors.Add(new AnalysisError
                {
                    ErrorType = "Синтаксическая ошибка",
                    Message = $"Неожиданный токен '{tok.Value}'. Ожидался операнд.",
                    Line = tok.Line,
                    Position = tok.Position
                });
                hasErrors = true;
                return "error";
            }

            // Вычисление ПОЛИЗ (алгоритм Дейкстры)
            private int? EvaluatePoliz()
            {
                if (!allIntegers || poliz.Count == 0)
                    return null;

                try
                {
                    var stack = new Stack<int>();
                    foreach (string item in poliz)
                    {
                        if (int.TryParse(item, out int num))
                        {
                            stack.Push(num);
                            continue;
                        }

                        if (stack.Count < 2)
                            return null;

                        int b = stack.Pop();
                        int a = stack.Pop();
                        int res;

                        switch (item)
                        {
                            case "+": res = a + b; break;
                            case "-": res = a - b; break;
                            case "*": res = a * b; break;
                            case "/":
                            case "//":
                                if (b == 0) return null;
                                res = a / b;
                                break;
                            case "%":
                                if (b == 0) return null;
                                res = a % b;
                                break;
                            case "**": res = (int)Math.Pow(a, b); break;
                            default: return null;
                        }
                        stack.Push(res);
                    }
                    return stack.Count == 1 ? stack.Pop() : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        // =============================================
        // ЛОГИКА ОКНА
        // =============================================

        private void InitializeNewDocument()
        {
            EditorBox.Document = new FlowDocument();
            EditorBox.AppendText("3 + 4 * 2 ** 3 // (1 - 5) + 10 % 3");
            EditorBox.Focus();
            UpdateStatusBar();
        }

        private void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
            {
                EditorBox.Document = new FlowDocument();
                currentFilePath = null;
                isTextChanged = false;
                UpdateStatusBar();
                StatusText.Text = "Создан новый документ";
                ClearResults();
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptSaveChanges()) return;
            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Открыть файл"
            };
            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(openDialog.FileName);
                    EditorBox.Document = new FlowDocument();
                    EditorBox.AppendText(content);
                    currentFilePath = openDialog.FileName;
                    isTextChanged = false;
                    FileInfoText.Text = Path.GetFileName(currentFilePath);
                    StatusText.Text = $"Файл загружен: {Path.GetFileName(currentFilePath)}";
                    ClearResults();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при открытии файла: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                SaveAsFile_Click(sender, e);
            else
                SaveFile(currentFilePath);
        }

        private void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Сохранить файл как"
            };
            if (saveDialog.ShowDialog() == true)
                SaveFile(saveDialog.FileName);
        }

        private void SaveFile(string filePath)
        {
            try
            {
                TextRange range = new TextRange(EditorBox.Document.ContentStart,
                    EditorBox.Document.ContentEnd);
                File.WriteAllText(filePath, range.Text);
                currentFilePath = filePath;
                isTextChanged = false;
                FileInfoText.Text = Path.GetFileName(currentFilePath);
                StatusText.Text = "Файл сохранён";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
                Application.Current.Shutdown();
        }

        private bool PromptSaveChanges()
        {
            if (!isTextChanged) return true;
            var result = MessageBox.Show("Сохранить изменения в файле?",
                "Сохранение", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SaveFile_Click(null, null);
                return true;
            }
            return result != MessageBoxResult.Cancel;
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => EditorBox.Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => EditorBox.Redo();
        private void Cut_Click(object sender, RoutedEventArgs e) => EditorBox.Cut();
        private void Copy_Click(object sender, RoutedEventArgs e) => EditorBox.Copy();
        private void Paste_Click(object sender, RoutedEventArgs e) => EditorBox.Paste();
        private void Delete_Click(object sender, RoutedEventArgs e) =>
            EditorBox.Selection.Text = string.Empty;
        private void SelectAll_Click(object sender, RoutedEventArgs e) => EditorBox.SelectAll();

        private void StartAnalysis_Click(object sender, RoutedEventArgs e)
        {
            ClearResults();
            StatusText.Text = "Выполняется анализ...";

            TextRange range = new TextRange(EditorBox.Document.ContentStart,
                EditorBox.Document.ContentEnd);
            string text = range.Text.Trim();

            if (string.IsNullOrEmpty(text))
            {
                ErrorsGrid.ItemsSource = new List<AnalysisError>
                {
                    new AnalysisError
                    {
                        ErrorType = "Ошибка ввода",
                        Message = "Пустая строка. Введите арифметическое выражение.",
                        Line = 1,
                        Position = 1
                    }
                };
                ErrorCountText.Text = "Общее количество ошибок: 1";
                StatusText.Text = "⚠ Пустая строка. Введите выражение.";
                ResultsTabControl.SelectedIndex = 4;
                return;
            }

            try
            {
                // Лексический анализ
                var (tokens, lexErrors) = lexer.Tokenize(text);
                TokensGrid.ItemsSource = tokens;

                if (lexErrors.Count > 0)
                {
                    ErrorsGrid.ItemsSource = lexErrors;
                    ErrorCountText.Text = $"Общее количество ошибок: {lexErrors.Count}";
                    StatusText.Text = $"✗ Найдено лексических ошибок: {lexErrors.Count}";
                    ResultsTabControl.SelectedIndex = 4;
                    return;
                }

                // Синтаксический анализ + тетрады + ПОЛИЗ
                var (tetrads, poliz, synErrors, result) = parser.Parse(tokens);

                if (synErrors.Count > 0)
                {
                    ErrorsGrid.ItemsSource = synErrors;
                    ErrorCountText.Text = $"Общее количество ошибок: {synErrors.Count}";
                    StatusText.Text = $"✗ Найдено синтаксических ошибок: {synErrors.Count}";
                    ResultsTabControl.SelectedIndex = 4;
                    return;
                }

                // Отображение результатов
                TetradsGrid.ItemsSource = tetrads;

                if (poliz.Count > 0)
                {
                    // ПОЛИЗ построен (все целые числа)
                    PolizText.Text = string.Join(" ", poliz);
                }
                else
                {
                    // ПОЛИЗ не строился (есть идентификаторы)
                    PolizText.Text = "ПОЛИЗ не построен (в выражении есть идентификаторы)";
                }

                if (result.HasValue)
                {
                    ResultText.Text = $"Значение выражения: {result.Value}";
                    StatusText.Text = "✓ Анализ успешно завершён. Выражение вычислено.";
                    ResultsTabControl.SelectedIndex = 3;
                }
                else if (poliz.Count > 0 && !result.HasValue)
                {
                    ResultText.Text = "Ошибка при вычислении (возможно, деление на ноль).";
                    StatusText.Text = "✓ Тетрады и ПОЛИЗ построены. Ошибка вычисления.";
                    ResultsTabControl.SelectedIndex = 2;
                }
                else
                {
                    ResultText.Text = "Вычисление невозможно.\nВ выражении присутствуют идентификаторы.";
                    StatusText.Text = "✓ Анализ завершён. Тетрады построены.";
                    ResultsTabControl.SelectedIndex = 1;
                }

                ErrorCountText.Text = "Общее количество ошибок: 0";
                ErrorsGrid.ItemsSource = new List<AnalysisError>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при анализе: {ex.Message}\n\n{ex.StackTrace}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка анализа";
            }
        }

        private void ClearResults()
        {
            TokensGrid.ItemsSource = new List<Token>();
            TetradsGrid.ItemsSource = new List<Tetrad>();
            PolizText.Text = "";
            ResultText.Text = "";
            ErrorsGrid.ItemsSource = new List<AnalysisError>();
            ErrorCountText.Text = "Общее количество ошибок: 0";
            ResultsTabControl.SelectedIndex = 0;
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            isTextChanged = true;
            UpdateStatusBar();
            ClearResults();
        }

        private void UpdateStatusBar()
        {
            try
            {
                TextPointer caret = EditorBox.CaretPosition;
                TextPointer lineStart = caret.GetLineStartPosition(0);
                int line = 1;
                TextPointer walker = lineStart;
                while (walker != null)
                {
                    TextPointer prev = walker.GetLineStartPosition(-1);
                    if (prev == null) break;
                    walker = prev;
                    line++;
                }
                int col = 1;
                TextPointer temp = lineStart;
                while (temp != null && temp.CompareTo(caret) < 0)
                {
                    TextPointer next = temp.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    temp = next;
                    col++;
                }
                CursorPositionText.Text = $"Стр: {line}, Стб: {col}";
            }
            catch
            {
                CursorPositionText.Text = "Стр: 1, Стб: 1";
            }
            FileInfoText.Text = string.IsNullOrEmpty(currentFilePath)
                ? "Новый документ"
                : Path.GetFileName(currentFilePath);
            if (isTextChanged && !FileInfoText.Text.EndsWith("*"))
                FileInfoText.Text += "*";
        }

        // Информационные окна
        private void TaskDescription_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Постановка задачи",
                "ЛАБОРАТОРНАЯ РАБОТА №6\nСоздание внутренней формы представления программы\n\n" +
                "Реализовано:\n• Лексический анализ\n• Синтаксический анализ (рекурсивный спуск)\n" +
                "• Генерация тетрад\n• Построение ПОЛИЗ\n• Вычисление значения\n\n" +
                "Приоритет: ** > * / // % > + -");
        }

        private void Grammar_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Грамматика",
                "E  → T A\nA  → ε | + T A | - T A\nT  → F B\n" +
                "B  → ε | * F B | / F B | // F B | % F B | ** F B\nF  → num | id | ( E )");
        }

        private void GrammarClassification_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Классификация",
                "Тип 2 по Хомскому — контекстно-свободная грамматика.\nНе леворекурсивная → подходит для рекурсивного спуска.");
        }

        private void AnalysisMethod_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Метод анализа",
                "Рекурсивный спуск.\nКаждому нетерминалу — метод.\n" +
                "Тетрады — при разборе.\nПОЛИЗ — параллельно.\nВычисление — стек (Дейкстра).");
        }

        private void TestExample_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Тестовые примеры",
                "✅ 3 + 5               → 8\n✅ 2 + 3 * 4           → 14\n✅ (2+3)*4             → 20\n" +
                "✅ 2**3**2             → 512\n✅ 17//5               → 3\n✅ 17%5                → 2\n" +
                "⚠ abc+5*2            → тетрады, без ПОЛИЗ\n❌ 3+*4               → ошибка\n❌ (5+3               → ошибка");
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Справка",
                "F5 — запуск анализа\nДопустимы: числа, идентификаторы, + - * / // % ** ( )");
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("О программе",
                "Лабораторная работа №6\nВнутреннее представление программы\nВариант 10 (Python)");
        }

        private void ShowInfoWindow(string title, string content)
        {
            Window infoWindow = new Window
            {
                Title = title,
                Content = new TextBox
                {
                    Text = content,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 650,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            infoWindow.ShowDialog();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!PromptSaveChanges())
                e.Cancel = true;
            base.OnClosing(e);
        }
    }
}