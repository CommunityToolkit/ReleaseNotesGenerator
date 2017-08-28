using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseNotes
{
    class Program
    {
        static GitHubClient _client = new GitHubClient(new ProductHeaderValue("uct-release-notes"));
        static Dictionary<string, User> _cachedUsers = new Dictionary<string, User>();
        static string[] _labels = { "animations", "controls", "extensions", "services", "helpers", "connectivity", "notifications", "documentation", "sample app" };

        const string _clientId = "[CLIENT-ID]";
        const string _clientSecret = "[CLIENT-SECRET]";

        static string _filename = "token";

        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "-auth")
            {
                Authorize().GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length < 2)
            {
                System.Console.WriteLine("\r\nUsage: releasenotes <Repo Owner> <Repo Name>\r\n");
                return 1;
            }

            if (File.Exists(_filename))
            {
                var token = File.ReadAllText(_filename);
                _client.Credentials = new Credentials(token);
            }

            var task = PrintReleaseNotes(args[0], args[1]);
            Console.Write(task.GetAwaiter().GetResult());

            return 0;
        }

        public static async Task Authorize()
        {
            var uri = _client.Oauth.GetGitHubLoginUrl(new OauthLoginRequest(_clientId));
            Process.Start(new ProcessStartInfo("cmd", $"/c start {uri.ToString()}"));
            Console.Write($"\r\nLaunching browser at {uri}.\r\nPlease log in and authorize application.\r\nAfter authorization, you will be redirected to an url that ends with code=[some_code]\r\nPlease copy the code, paste it here, and press enter.\r\n\r\ncode=");
            var code = Console.ReadLine();
            Console.Write("\r\n");

            if (String.IsNullOrEmpty(code))
                return;

            var request = new OauthTokenRequest(_clientId, _clientSecret, code);
            var token = await _client.Oauth.CreateAccessToken(request);

            File.WriteAllText(_filename, token.AccessToken);
        }

        private static async Task<string> PrintReleaseNotes(string repoOwner, string repoName)
        {
            var result = new StringBuilder();
            var notes = new Dictionary<string, List<string>>();

            foreach (var labelName in _labels)
            {
                notes.Add(labelName, new List<string>());
            }
            notes.Add("other", new List<string>());
            notes.Add("breaking changes", new List<string>());

            try
            {
                var release = await _client.Repository.Release.GetLatest(repoOwner, repoName);
                var request = new PullRequestRequest()
                {
                    State = ItemStateFilter.Closed,
                    SortProperty = PullRequestSort.Updated
                };
                var options = new ApiOptions()
                {
                    PageCount = 1,
                    StartPage = 1
                };

                var prs = await _client.PullRequest.GetAllForRepository(repoOwner, repoName, request, options);
                var actionablePRs = prs.Where(pr => pr.Merged && pr.MergedAt > release.PublishedAt);
                do
                {
                    foreach (var pr in actionablePRs)
                    {
                        if (!_cachedUsers.TryGetValue(pr.User.Login, out var user))
                        {
                            user = await _client.User.Get(pr.User.Login);
                            _cachedUsers.Add(pr.User.Login, user);
                        }

                        var line = $"\t- {pr.Title} - [{user.Name ?? user.Login}]({user.HtmlUrl}) ([PR]({pr.HtmlUrl}))";

                        var issue = await _client.Issue.Get(repoOwner, repoName, pr.Number);
                        var matchedLabel = issue.Labels.Where(i => _labels.Contains(i.Name)).FirstOrDefault();

                        notes[matchedLabel != null ? matchedLabel.Name : "other"].Add(line);

                        if (issue.Labels.Where(i => i.Name == "introduce breaking changes").Count() > 0)
                            notes["breaking changes"].Add(line);
                    }

                    options.StartPage++;
                    prs = await _client.PullRequest.GetAllForRepository("Microsoft", "UWPCommunityToolkit", request, options);
                    actionablePRs = prs.Where(pr => pr.Merged && pr.MergedAt > release.PublishedAt);
                } while (actionablePRs.Count() > 0);
            }
            catch (Octokit.NotFoundException)
            {
                return "Repository not found, or repository does not have milestones or pull requests\r\n";
            }
            catch (Octokit.RateLimitExceededException ex)
            {
                return $"\r\n{ex.Message}\r\n\r\nTo authenticate, run: ReleaseNotes -auth\r\n\r\n";
            }

            foreach (var label in notes)
            {
                if (label.Value.Count > 0)
                {
                    result.AppendLine($"- {label.Key}");
                    foreach (var line in label.Value)
                    {
                        result.AppendLine(line);
                    }
                    result.AppendLine();
                }
            }

            return result.ToString();
        }
    }
}
