using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class ModelFilterService
{
	private readonly string _configPath;
	public ObservableCollection<ModelFilterEntry> TrackedModels { get; } = new();
	public List<string> ExcludeKeywords { get; private set; } = new() { "agent", "image", "lite" };

	public ModelFilterService()
	{
		_configPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AIUsageMonitor", "model_filters.json");
		LoadConfig();
	}

	private void LoadConfig()
	{
		try
		{
			if (File.Exists(_configPath))
			{
				var json = File.ReadAllText(_configPath);
				var config = JsonSerializer.Deserialize<ModelFilterConfig>(json);
				if (config != null)
				{
					TrackedModels.Clear();
					foreach (var m in config.TrackedModels)
						TrackedModels.Add(m);

					if (config.ExcludeKeywords.Count > 0)
						ExcludeKeywords = config.ExcludeKeywords;

					Debug.WriteLine($"[ModelFilter] Loaded {TrackedModels.Count} filters from disk.");
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[ModelFilter] Load error: {ex.Message}");
		}

		// Defaults
		LoadDefaults();
	}

	private void LoadDefaults()
	{
		TrackedModels.Clear();
		var defaults = new[]
		{
			("gemini-3-flash", "Gemini 3 Flash"),
			("gemini-3.1-pro", "Gemini 3.1 Pro"),
			("claude", "Claude"),
			("gpt", "GPT-OSS 120B"),
		};

		foreach (var (keyword, displayName) in defaults)
		{
			TrackedModels.Add(new ModelFilterEntry
			{
				Keyword = keyword,
				DisplayName = displayName,
				Enabled = true
			});
		}

		ExcludeKeywords = new() { "agent", "image", "lite" };
		SaveConfig();
	}

	public void SaveConfig()
	{
		try
		{
			var dir = Path.GetDirectoryName(_configPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			var config = new ModelFilterConfig
			{
				TrackedModels = TrackedModels.ToList(),
				ExcludeKeywords = ExcludeKeywords
			};

			var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(_configPath, json);
			Debug.WriteLine($"[ModelFilter] Saved {TrackedModels.Count} filters.");
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[ModelFilter] Save error: {ex.Message}");
		}
	}

	public void AddEntry(string keyword, string displayName)
	{
		TrackedModels.Add(new ModelFilterEntry
		{
			Keyword = keyword,
			DisplayName = displayName,
			Enabled = true
		});
		SaveConfig();
	}

	public void RemoveEntry(ModelFilterEntry entry)
	{
		TrackedModels.Remove(entry);
		SaveConfig();
	}

	public void MoveUp(ModelFilterEntry entry)
	{
		var idx = TrackedModels.IndexOf(entry);
		if (idx > 0)
		{
			TrackedModels.Move(idx, idx - 1);
			SaveConfig();
		}
	}

	public void MoveDown(ModelFilterEntry entry)
	{
		var idx = TrackedModels.IndexOf(entry);
		if (idx < TrackedModels.Count - 1)
		{
			TrackedModels.Move(idx, idx + 1);
			SaveConfig();
		}
	}

	public void ResetToDefaults()
	{
		LoadDefaults();
	}

	// Used by GoogleApiService
	public bool IsTrackedModel(string modelName)
	{
		var clean = modelName.Replace("models/", "").ToLower();

		// Check exclusions first
		foreach (var exclude in ExcludeKeywords)
		{
			if (clean.Contains(exclude.ToLower())) return false;
		}

		// Check enabled keywords
		foreach (var entry in TrackedModels)
		{
			if (entry.Enabled && clean.Contains(entry.Keyword.ToLower())) return true;
		}

		return false;
	}

	public string GetDisplayName(string modelName)
	{
		var clean = modelName.Replace("models/", "").ToLower();

		foreach (var entry in TrackedModels)
		{
			if (entry.Enabled && clean.Contains(entry.Keyword.ToLower()))
				return entry.DisplayName;
		}

		return modelName;
	}

	public int GetSortOrder(string displayName)
	{
		for (int i = 0; i < TrackedModels.Count; i++)
		{
			if (TrackedModels[i].DisplayName == displayName) return i;
		}
		return 99;
	}
}
