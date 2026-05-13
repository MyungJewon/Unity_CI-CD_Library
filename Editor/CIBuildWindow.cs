using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

public class CIBuildWindow : EditorWindow
{
    enum Platform { Android, iOS, Windows }
    enum ServerOS { Mac, Windows }

    string   _projectPath = "";
    string   _outputPath  = "";
    Platform _platform    = Platform.Android;
    ServerOS _serverOs    = ServerOS.Mac;
    bool     _isBuilding  = false;
    Vector2  _logScroll;
    string   _logText     = "";
    readonly ConcurrentQueue<string> _pendingLines = new ConcurrentQueue<string>();

    // SSH 설정
    bool   _showSshSettings = false;
    string _sshHost         = "";
    string _sshUser         = "";
    string _sshKeyPath      = "";

    string   _userId      = "";
    string   _branch      = "main";
    string[] _branches    = { "main" };
    int      _branchIndex = 0;
    bool     _isFetching  = false;

    // 프로그래스
    float    _buildProgress  = 0f;
    string   _buildStep      = "";
    DateTime _buildStartTime;
    double   _lastRepaintTime;

    [MenuItem("Window/CI Build")]
    static void Open() => GetWindow<CIBuildWindow>("CI Build");

    void OnEnable()
    {
        _projectPath = EditorPrefs.GetString("CIBuild_ProjectPath", "");
        _outputPath  = EditorPrefs.GetString("CIBuild_OutputPath",  "");
        _platform    = (Platform)EditorPrefs.GetInt("CIBuild_Platform",  0);
        _serverOs    = (ServerOS)EditorPrefs.GetInt("CIBuild_ServerOs",  0);
        _sshHost     = EditorPrefs.GetString("CIBuild_SshHost",    "");
        _sshUser     = EditorPrefs.GetString("CIBuild_SshUser",    "");
        _sshKeyPath  = EditorPrefs.GetString("CIBuild_SshKeyPath", "");
        _userId      = EditorPrefs.GetString("CIBuild_UserId",     "");
        _branch      = EditorPrefs.GetString("CIBuild_Branch",     "main");

        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        EditorPrefs.SetString("CIBuild_ProjectPath", _projectPath);
        EditorPrefs.SetString("CIBuild_OutputPath",  _outputPath);
        EditorPrefs.SetInt("CIBuild_Platform",        (int)_platform);
        EditorPrefs.SetInt("CIBuild_ServerOs",        (int)_serverOs);
        EditorPrefs.SetString("CIBuild_SshHost",     _sshHost);
        EditorPrefs.SetString("CIBuild_SshUser",     _sshUser);
        EditorPrefs.SetString("CIBuild_SshKeyPath",  _sshKeyPath);
        EditorPrefs.SetString("CIBuild_UserId",      _userId);
        EditorPrefs.SetString("CIBuild_Branch",      _branch);

        EditorApplication.update -= OnEditorUpdate;
    }

    // 빌드 중 0.5초마다 Repaint → 소요 시간 실시간 갱신
    void OnEditorUpdate()
    {
        if (_isBuilding && EditorApplication.timeSinceStartup - _lastRepaintTime > 0.5)
        {
            _lastRepaintTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    void OnGUI()
    {
        FlushPendingLog();

        GUILayout.Label("CI Build", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        DrawSshSettings();
        EditorGUILayout.Space(5);

        _userId = EditorGUILayout.TextField("사용자 ID", _userId);
        DrawPathField("Build 프로젝트", ref _projectPath);
        DrawPathField("Build 출력 폴더", ref _outputPath);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_isFetching))
            {
                _branchIndex = EditorGUILayout.Popup("브랜치", _branchIndex, _branches);
                _branch      = _branches[_branchIndex];

                if (GUILayout.Button(_isFetching ? "..." : "Fetch", GUILayout.Width(50)))
                    FetchBranches();
            }
        }

        EditorGUILayout.Space(5);

        _platform = (Platform)EditorGUILayout.EnumPopup("플랫폼", _platform);

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(_isBuilding))
        {
            if (GUILayout.Button(_isBuilding ? "빌드 중..." : "Build", GUILayout.Height(32)))
                StartBuild();
        }

        // ── 프로그래스 바 ──────────────────────────────────────────────────────
        if (_buildProgress > 0f)
        {
            EditorGUILayout.Space(6);

            Rect barRect = EditorGUILayout.GetControlRect(false, 24f);
            EditorGUI.ProgressBar(barRect, _buildProgress, _buildStep);

            TimeSpan elapsed = (_isBuilding ? DateTime.Now : _buildStartTime + _buildElapsed) - _buildStartTime;
            string elapsedStr = $"소요 시간  {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            GUILayout.Label(elapsedStr, EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(2);
        }

        // ── 로그 ───────────────────────────────────────────────────────────────
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Log", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
                _logText = "";
        }

        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(_logText, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        EditorGUILayout.EndScrollView();
    }

    void DrawSshSettings()
    {
        _showSshSettings = EditorGUILayout.Foldout(_showSshSettings, "SSH 설정", true);
        if (!_showSshSettings) return;

        EditorGUI.indentLevel++;

        _serverOs = (ServerOS)EditorGUILayout.EnumPopup("서버 OS", _serverOs);
        _sshHost  = EditorGUILayout.TextField("Host (IP)", _sshHost);
        _sshUser  = EditorGUILayout.TextField("User",      _sshUser);

        using (new EditorGUILayout.HorizontalScope())
        {
            _sshKeyPath = EditorGUILayout.TextField("SSH Key 경로", _sshKeyPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string selected = EditorUtility.OpenFilePanel("SSH Key 선택", "~/.ssh", "");
                if (!string.IsNullOrEmpty(selected))
                    _sshKeyPath = selected;
            }
        }

        EditorGUI.indentLevel--;
    }

    void DrawPathField(string label, ref string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string selected = EditorUtility.OpenFolderPanel(label, value, "");
                if (!string.IsNullOrEmpty(selected))
                    value = selected;
            }
        }
    }

    void StartBuild()
    {
        if (!Validate()) return;

        _isBuilding      = true;
        _buildProgress   = 0.05f;
        _buildStep       = "연결 중...";
        _buildStartTime  = DateTime.Now;
        _buildElapsed    = TimeSpan.Zero;
        _logText         = "";

        AppendLog($"[CI] 빌드 시작 - {_platform}");
        AppendLog($"[CI] 호스트: {_sshUser}@{_sshHost}");
        AppendLog($"[CI] 대상: {_projectPath}");
        AppendLog($"[CI] 출력: {_outputPath}");

        var    pkg         = PackageInfo.FindForAssembly(typeof(CIBuildWindow).Assembly);
        string buildSh     = Path.Combine(pkg.resolvedPath, "Shell", "build.sh");
        string platformArg = _platform switch
        {
            Platform.Android => "android",
            Platform.iOS     => "ios",
            Platform.Windows => "windows",
            _                => "android",
        };

        AppendLog($"[CI] Script: {buildSh}");

        string templatePath    = Path.Combine(pkg.resolvedPath, "Templates", "BuildScript.cs.template");
        string templateContent = File.ReadAllText(templatePath);

        string scriptHeader = $"export LANG=en_US.UTF-8\n" +
                              $"export LC_ALL=en_US.UTF-8\n" +
                              $"export PROJECT_PATH={BashQuote(_projectPath)}\n" +
                              $"export OUTPUT_PATH={BashQuote(_outputPath)}\n" +
                              $"export PLATFORM={BashQuote(platformArg)}\n" +
                              $"export BRANCH={BashQuote(_branch)}\n" +
                              $"export USER_ID={BashQuote(_userId)}\n" +
                              $"export SERVER_OS={BashQuote(_serverOs == ServerOS.Windows ? "windows" : "mac")}\n" +
                              $"export TEMPLATE_PATH=/tmp/ci_BuildScript.cs.template\n" +
                              $"cat > /tmp/ci_BuildScript.cs.template << 'CI_TEMPLATE_EOF'\n" +
                              templateContent + "\n" +
                              "CI_TEMPLATE_EOF\n";

        string keyArg      = string.IsNullOrWhiteSpace(_sshKeyPath) ? "" : $"-i \"{_sshKeyPath}\" ";
        string remoteShell = _serverOs == ServerOS.Windows
            ? "\"\\\"C:\\\\Program Files\\\\Git\\\\bin\\\\bash.exe\\\" -s\""
            : "\"bash -s\"";
        string sshArgs = $"{keyArg}-o StrictHostKeyChecking=no {_sshUser}@{_sshHost} {remoteShell}";

        var psi = new ProcessStartInfo
        {
            FileName                = "ssh",
            Arguments               = sshArgs,
            UseShellExecute         = false,
            RedirectStandardInput   = true,
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            CreateNoWindow          = true,
            StandardOutputEncoding  = System.Text.Encoding.UTF8,
            StandardErrorEncoding   = System.Text.Encoding.UTF8,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) _pendingLines.Enqueue(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) _pendingLines.Enqueue("[ERR] " + e.Data); };
        process.Exited += (_, __) =>
        {
            int code = process.ExitCode;
            _pendingLines.Enqueue(code == 0
                ? "[CI] 빌드 성공!"
                : $"[CI] 빌드 실패 (exit {code})");
            _buildElapsed = DateTime.Now - _buildStartTime;
            _isBuilding   = false;
            process.Dispose();
        };

        process.Start();
        process.StandardInput.Write(scriptHeader + File.ReadAllText(buildSh));
        process.StandardInput.Flush();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    // 빌드 종료 후에도 소요 시간을 고정 표시하기 위해 저장
    TimeSpan _buildElapsed = TimeSpan.Zero;

    static string BashQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    void FetchBranches()
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            AppendLog("[CI] ERROR: 브랜치 목록을 가져오려면 먼저 프로젝트 경로를 입력하세요.");
            return;
        }

        _isFetching = true;

        var psi = new ProcessStartInfo
        {
            FileName               = "git",
            Arguments              = $"-C \"{_projectPath}\" branch -r --format=%(refname:short)",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var lines   = new System.Collections.Generic.List<string>();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Add(e.Data); };
        process.Exited += (_, __) =>
        {
            var branches = lines
                .FindAll(l => !l.Contains("->"))
                .ConvertAll(l => l.Trim().Replace("origin/", ""));

            if (branches.Count == 0)
                branches.Add("main");

            EditorApplication.delayCall += () =>
            {
                _branches    = branches.ToArray();
                _branchIndex = System.Math.Max(0, branches.IndexOf(_branch));
                _branch      = _branches[_branchIndex];
                _isFetching  = false;
                Repaint();
            };

            process.Dispose();
        };

        process.Start();
        process.BeginOutputReadLine();
    }

    bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_sshHost))
        {
            AppendLog("[CI] ERROR: SSH Host (IP)를 입력하세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_sshUser))
        {
            AppendLog("[CI] ERROR: SSH User를 입력하세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_userId))
        {
            AppendLog("[CI] ERROR: 사용자 ID를 입력하세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            AppendLog("[CI] ERROR: 빌드 대상 프로젝트 경로를 입력하세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            AppendLog("[CI] ERROR: 빌드 출력 폴더를 입력하세요.");
            return false;
        }

        return true;
    }

    void AppendLog(string line)
    {
        _logText += line + "\n";
        _logScroll.y = float.MaxValue;
        Repaint();
    }

    void FlushPendingLog()
    {
        bool dirty = false;
        while (_pendingLines.TryDequeue(out string line))
        {
            _logText += line + "\n";
            UpdateProgress(line);
            dirty = true;
        }
        if (dirty)
        {
            _logScroll.y = float.MaxValue;
            Repaint();
        }
    }

    void UpdateProgress(string line)
    {
        if      (line.Contains("git fetch origin"))                              { _buildProgress = 0.10f; _buildStep = "Git fetch..."; }
        else if (line.Contains("resetting to clean state"))                      { _buildProgress = 0.18f; _buildStep = "Git reset..."; }
        else if (line.Contains("Reset complete"))                                { _buildProgress = 0.22f; _buildStep = "Git reset 완료"; }
        else if (line.Contains("git pull"))                                      { _buildProgress = 0.25f; _buildStep = "Git pull..."; }
        else if (line.Contains("Already up to date"))                            { _buildProgress = 0.28f; _buildStep = "Git 최신 상태"; }
        else if (line.Contains("Unity version :"))                               { _buildProgress = 0.38f; _buildStep = "Unity 버전 감지"; }
        else if (line.Contains("Injected BuildScript"))                          { _buildProgress = 0.48f; _buildStep = "BuildScript 주입"; }
        else if (line.Contains("Build output :"))                                { _buildProgress = 0.52f; _buildStep = "출력 폴더 준비"; }
        else if (line.Contains("Starting build"))                                { _buildProgress = 0.58f; _buildStep = "Unity 빌드 중..."; }
        else if (line.Contains("OpenXR settings not loaded"))                    { _buildProgress = 0.72f; _buildStep = "OpenXR 재시도 중..."; }
        else if (line.Contains("Cleaning up"))                                   { _buildProgress = 0.96f; _buildStep = "정리 중..."; }
        else if (line.Contains("빌드 성공") || line.Contains("Build succeeded")) { _buildProgress = 1.00f; _buildStep = "빌드 성공!"; }
        else if (line.Contains("빌드 실패") || line.Contains("Build FAILED"))   { _buildProgress = 1.00f; _buildStep = "빌드 실패"; }
    }
}
