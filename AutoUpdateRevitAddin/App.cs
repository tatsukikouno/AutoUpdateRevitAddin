using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace AutoUpdateRevitAddin
{
    class App : IExternalApplication
    {
        // 最新リリース情報取得のためのGitHub API の URL
        const string githubApiUrl = "https://api.github.com/repos/tatsukikouno/AutoUpdateRevitAddin/releases/latest";
        const string deleteFileName = "0BA5B119-965B-4C4F-A210-6DF05E464EE9";

        public Result OnStartup(UIControlledApplication ap)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var assemblyLocation = assembly.Location;
                if (assembly.GetName().Name is not string assemblyName)
                {
                    TaskDialog.Show("アップデート確認", "最新バージョンを利用しています。");
                    return Result.Succeeded;
                }

                // {deleteFileName}.dll が存在する場合は削除
                var deleteTarget = assemblyLocation.Replace(assemblyName, deleteFileName);
                if (File.Exists(deleteTarget))
                {
                    File.Delete(deleteTarget);
                }

                // HttpClient の初期化（GitHub API では User-Agent ヘッダーが必要）
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd(assemblyName);

                // UpdateChecker を初期化
                var updateChecker = new UpdateChecker(client, githubApiUrl);

                // 現在のバージョンを取得
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                // 最新バージョンを取得（非同期処理を同期的に待機）
                var latestVersion = updateChecker.GetLatestVersionAsync().GetAwaiter().GetResult();

                if (latestVersion > currentVersion)
                {
                    // 新しいアップデートがある場合、ユーザーに確認
                    var dialog = new TaskDialog("アップデート確認");
                    dialog.MainInstruction = $"新しいアップデート (v{latestVersion}) が利用可能です。";
                    dialog.MainContent = "アップデートを適用しますか？";
                    dialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    TaskDialogResult result = dialog.Show();

                    if (result == TaskDialogResult.Yes)
                    {
                        // 最新リリースの AutoUpdateRevitAddin.dll をダウンロードする URL を構築
                        string downloadUrl = $"https://github.com/tatsukikouno/AutoUpdateRevitAddin/releases/download/v{latestVersion}/AutoUpdateRevitAddin.dll";

                        // 一時フォルダーにダウンロード（ファイル名は固定）
                        var tempPath = Path.Combine(Path.GetTempPath(), "AutoUpdateRevitAddin.dll");
                        var fileBytes = client.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();
                        File.WriteAllBytes(tempPath, fileBytes);

                        // 現在のアセンブリを{deleteFileName}.dllに変更
                        File.Move(assemblyLocation, deleteTarget);
                        File.Move(tempPath, assemblyLocation);
                        TaskDialog.Show("アップデート確認", "最新バージョンのダウンロードが完了しました。アドインはRevit再起動時に更新されます。");
                    }
                }
                else
                {
                    TaskDialog.Show("アップデート確認", "最新バージョンを利用しています。");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("エラー", "アップデートチェック中にエラーが発生しました: " + ex.Message);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }

    // GitHub API を利用して最新リリースの tag_name を取得するクラス
    public class UpdateChecker
    {
        private readonly HttpClient _httpClient;
        private readonly string _githubApiUrl;
        private readonly string? _githubToken;

        public UpdateChecker(HttpClient httpClient, string githubApiUrl, string? githubToken = null)
        {
            _httpClient = httpClient;
            _githubApiUrl = githubApiUrl;
            _githubToken = githubToken;
        }

        // GitHub APIから最新のバージョン（tag_name）を取得
        public async Task<Version> GetLatestVersionAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _githubApiUrl);
            if (!string.IsNullOrEmpty(_githubToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _githubToken);
            }
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // GitHub API のレスポンス例: { "tag_name": "v2.0.0", ... }
            var json = await response.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            if (obj["tag_name"] is not JToken token)
            {
                throw new Exception("tag_name not found in response.");
            }
            var tagName = token.ToString();
            if (tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                tagName = tagName.Substring(1);
            }
            return Version.Parse(tagName);
        }

        // 現在のバージョンと最新バージョンを比較し、更新が必要かどうかを返す
        public async Task<bool> IsUpdateAvailableAsync(Version currentVersion)
        {
            var latestVersion = await GetLatestVersionAsync();
            return latestVersion > currentVersion;
        }
    }
}
