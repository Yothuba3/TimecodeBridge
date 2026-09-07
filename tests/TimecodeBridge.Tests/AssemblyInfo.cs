using Xunit;

// WPF の Application はプロセスに1つ・作成スレッドに固定される。
// 並列実行するとダイアログ系テストとテーマ系テストが別スレッドから取り合って落ちる。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
