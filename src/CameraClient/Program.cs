using Azure.Storage.Blobs;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CameraClient
{
    class Program
    {
        private static readonly string _connectionString = "xxxxxxxxxxxx";

        static async Task Main(string[] args)
        {
            try
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl("https://xxxxxx.azurewebsites.net/api")
                    .WithAutomaticReconnect()
                    .Build();

                connection.On<string>("requestVideo", async (lineUserId) =>
                {
                    await ExecuteCommandAsync(lineUserId);
                });

                await connection.StartAsync();

                Console.WriteLine("Connected to SignalR Service. Waiting for commands...");

                // プロセスが終了しないようにするために適切な時間を指定します
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to SignalR Service: {ex.Message}");
            }
        }

        static async Task ExecuteCommandAsync(string lineUserId)
        {
            try
            {
                Console.WriteLine($"lineUserId: {lineUserId}");

                // LINEユーザーID＋タイムスタンプでファイル名を生成
                var fileBaseName = $"{lineUserId}_{DateTime.Now:yyyyMMddHHmmss}";

                string[] commands = new string[]
                {
                    $"libcamera-vid -o {fileBaseName}.h264 -t 10000 --width 1920 --height 1080",
                    $"MP4Box -fps 30 -add {fileBaseName}.h264 {fileBaseName}.mp4"
                };

                foreach (string command in commands)
                {
                    Console.WriteLine(command);
                    var process = new Process()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "bash",
                            Arguments = $"-c \"{command}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    process.Start();
                    var result = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    Console.WriteLine(result);
                }

                // Blob Storageにアップロード
                var containerName = "videos";
                var localFilePath = $"{fileBaseName}.mp4";

                var containerClient = new BlobContainerClient(_connectionString, containerName);
                containerClient.CreateIfNotExists();

                await UploadFromFileAsync(containerClient, localFilePath);
                Console.WriteLine(
                    $"Uploaded {localFilePath} to blob storage as {localFilePath}");

                // ローカルファイルを削除
                System.IO.File.Delete(localFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing command: {ex.Message}");
            }
        }

        public static async Task UploadFromFileAsync(
            BlobContainerClient containerClient,
            string localFilePath)
        {
            string fileName = Path.GetFileName(localFilePath);
            BlobClient blobClient = containerClient.GetBlobClient(fileName);

            await blobClient.UploadAsync(localFilePath, true);
        }
    }
}
