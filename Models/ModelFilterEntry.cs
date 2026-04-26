using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Models;

public class ModelFilterEntry : INotifyPropertyChanged
{
	private string _keyword = "";
	private string _displayName = "";
	private bool _enabled = true;

	public string Keyword
	{
		get => _keyword;
		set { _keyword = value; OnPropertyChanged(); }
	}

	public string DisplayName
	{
		get => _displayName;
		set { _displayName = value; OnPropertyChanged(); }
	}

	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; OnPropertyChanged(); }
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ModelFilterConfig
{
	[JsonPropertyName("trackedModels")]
	public List<ModelFilterEntry> TrackedModels { get; set; } = new();

	[JsonPropertyName("excludeKeywords")]
	public List<string> ExcludeKeywords { get; set; } = new() { "agent", "image", "lite" };
}
