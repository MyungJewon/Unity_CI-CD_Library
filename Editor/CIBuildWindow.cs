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

    [MenuItem("Window/CI Build")]
    static void Open() => GetWindow<CIBuildWindow>("CI Build");

    void OnEnable()
    {
        _projectPath = EditorPrefs.GetString("CIBuild_ProjectPath", "");
        _outputPath  = EditorPrefs.GetString("CIBuild_OutputPath",  "");
        _platform    = (Platform)EditorPrefs.GetInt("CIBuild_Platform", 0);
    }

    void OnDisable()
    {
        EditorPrefs.SetString("CIBuild_ProjectPath", _projectPath);
        EditorPrefs.SetString("CIBuild_OutputPath",  _outputPath);
        EditorPrefs.SetInt("CIBuild_Platform",       (int)_platform);
    }

    void OnGUI()
    {
        FlushPendingLog();

        GUILayout.Label("CI Build", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        DrawPathField("Build 프로젝트", ref _projectPath);
        DrawPathField("Build 출력 폴더", ref _outputPath);

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
        if (!ValidatePaths()) return;

        _isBuilding = true;
        _logText    = "";
        AppendLog($"[CI] 빌드 시작 - {_platform}");
        AppendLog($"[CI] 대상: {_projectPath}");
        AppendLog($"[CI] 출력: {_outputPath}");

        // 패키지 설치 경로를 기준으로 build.sh / template 경로를 구성
        // PackageInfo.FindForAssembly: 이 스크립트가 속한 어셈블리의 패키지 정보를 반환
        var    pkg        = PackageInfo.FindForAssembly(typeof(CIBuildWindow).Assembly);
        string packagePath = pkg.resolvedPath;
        string buildSh     = Path.Combine(packagePath, "Shell", "build.sh");
        string platformArg = _platform == Platform.Android ? "android" : "ios";

        var psi = new ProcessStartInfo
        {
            FileName               = "/bin/bash",
            Arguments              = $"\"{buildSh}\" -project \"{_projectPath}\" -output \"{_outputPath}\" -platform {platformArg}",
            UseShellExecute        = false,
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
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    bool ValidatePaths()
    {
        if (string.IsNullOrWhiteSpace(_projectPath) || !Directory.Exists(_projectPath))
        {
            AppendLog("[CI] ERROR: 유효한 BuildTarget 프로젝트 경로를 입력하세요.");
            return false;
        }

        string versionFile = Path.Combine(_projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
        {
            AppendLog("[CI] ERROR: Unity 프로젝트가 아닙니다 (ProjectVersion.txt 없음).");
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
