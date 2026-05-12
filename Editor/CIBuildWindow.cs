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
    enum Platform { Android, iOS }
    string   _projectPath = "";
    string   _outputPath  = "";
    Platform _platform    = Platform.Android;
    bool     _isBuilding  = false;
    Vector2  _logScroll;
    string   _logText     = "";
    readonly ConcurrentQueue<string> _pendingLines = new ConcurrentQueue<string>();

    // SSH 설정
    bool   _showSshSettings = false;
    string _sshHost         = "";
    string _sshUser         = "";
    string _sshKeyPath      = "";

    string   _branch      = "main";
    string[] _branches    = { "main" };
    int      _branchIndex = 0;
    bool     _isFetching  = false;

    [MenuItem("Window/CI Build")]
    static void Open() => GetWindow<CIBuildWindow>("CI Build");

    void OnEnable()
    {
        _projectPath = EditorPrefs.GetString("CIBuild_ProjectPath", "");
        _outputPath  = EditorPrefs.GetString("CIBuild_OutputPath",  "");
        _platform    = (Platform)EditorPrefs.GetInt("CIBuild_Platform", 0);
        _sshHost    = EditorPrefs.GetString("CIBuild_SshHost",    "");
        _sshUser    = EditorPrefs.GetString("CIBuild_SshUser",    "");
        _sshKeyPath = EditorPrefs.GetString("CIBuild_SshKeyPath", "");
        _branch     = EditorPrefs.GetString("CIBuild_Branch",     "main");
    }

    void OnDisable()
    {
        EditorPrefs.SetString("CIBuild_ProjectPath", _projectPath);
        EditorPrefs.SetString("CIBuild_OutputPath",  _outputPath);
        EditorPrefs.SetInt("CIBuild_Platform",       (int)_platform);
        EditorPrefs.SetString("CIBuild_SshHost",    _sshHost);
        EditorPrefs.SetString("CIBuild_SshUser",    _sshUser);
        EditorPrefs.SetString("CIBuild_SshKeyPath", _sshKeyPath);
        EditorPrefs.SetString("CIBuild_Branch",     _branch);
    }

    void OnGUI()
    {
        FlushPendingLog();

        GUILayout.Label("CI Build", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        DrawSshSettings();
        EditorGUILayout.Space(5);

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

        EditorGUILayout.Space(4);

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

        _sshHost = EditorGUILayout.TextField("Host (IP)", _sshHost);
        _sshUser = EditorGUILayout.TextField("User",      _sshUser);

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

        _isBuilding = true;
        _logText    = "";
        AppendLog($"[CI] 빌드 시작 - {_platform}");
        AppendLog($"[CI] 호스트: {_sshUser}@{_sshHost}");
        AppendLog($"[CI] 대상: {_projectPath}");
        AppendLog($"[CI] 출력: {_outputPath}");

        var    pkg         = PackageInfo.FindForAssembly(typeof(CIBuildWindow).Assembly);
        string buildSh     = Path.Combine(pkg.resolvedPath, "Shell", "build.sh");
        string platformArg = _platform == Platform.Android ? "android" : "ios";

        string keyArg = string.IsNullOrWhiteSpace(_sshKeyPath)
            ? ""
            : $"-i \"{_sshKeyPath}\" ";

        // build.sh를 Mac Mini에 설치하지 않고, 개발 PC의 파일을 stdin으로 전송해 실행
        // bash -s : stdin을 스크립트로 읽음 / -- : 이후 인자를 $1 $2 ... 로 전달
        string sshArgs = $"{keyArg}-o StrictHostKeyChecking=no " +
                         $"{_sshUser}@{_sshHost} " +
                         $"bash -s -- " +
                         $"-project \"{_projectPath}\" " +
                         $"-output \"{_outputPath}\" " +
                         $"-platform {platformArg} " +
                         $"-branch \"{_branch}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = "ssh",
            Arguments              = sshArgs,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
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
            _isBuilding = false;
            process.Dispose();
        };

        process.Start();
        process.StandardInput.Write(File.ReadAllText(buildSh));
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

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
            // origin/HEAD -> origin/main 같은 항목 제거 후 origin/ 접두사 제거
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
            dirty = true;
        }
        if (dirty)
        {
            _logScroll.y = float.MaxValue;
            Repaint();
        }
    }
}
