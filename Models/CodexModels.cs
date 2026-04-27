using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Models;

public class CodexAccount : INotifyPropertyChanged
{
	private string _accessToken = string.Empty;
	private string _name = string.Empty;
	private string _email = string.Empty;
	private string _planType = string.Empty;
	private double _credits;
	private bool _isAnonymous;
	private bool _hasCredits;
	private bool _unlimitedCredits;
	private bool _isRefreshing;
	private bool _isRefreshQueued;
	private bool _hasError;

	// Primary window (the main usage gauge — often labeled "주간 사용 한도" on the site)
	private int _primaryUsedPercent;
	private string _primaryWindowLabel = "Usage Limit";
	private string _primaryResetDescription = string.Empty;

	// Secondary window (longer cycle — often shows a reset date, not a gauge)
	private int _secondaryUsedPercent;
	private string _secondaryWindowLabel = "Reset Date";
	private string _secondaryResetDate = string.Empty;

	public string id { get; set; } = Guid.NewGuid().ToString();

	public string name 
	{ 
		get => _name; 
		set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } 
	}

	public string email 
	{ 
		get => _email; 
		set { _email = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayEmail)); } 
	}
	
	public string access_token 
	{ 
		get => _accessToken; 
		set { _accessToken = value; OnPropertyChanged(); } 
	}

	public string refresh_token { get; set; } = string.Empty;
	public string account_id { get; set; } = string.Empty;
	public string login_method { get; set; } = "manual"; // "openai", "github", "manual"

	public DateTime? last_updated { get; set; }

	public string plan_type 
	{ 
		get => _planType; 
		set { _planType = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlanDisplay)); } 
	}

	public double credits 
	{ 
		get => _credits; 
		set { _credits = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreditsDisplay)); } 
	}

	public bool has_credits 
	{ 
		get => _hasCredits; 
		set { _hasCredits = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreditsDisplay)); } 
	}

	public bool unlimited_credits 
	{ 
		get => _unlimitedCredits; 
		set { _unlimitedCredits = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreditsDisplay)); } 
	}

	// Primary Window (main gauge)
	public int primaryUsedPercent 
	{ 
		get => _primaryUsedPercent; 
		set { _primaryUsedPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryRemainingPercent)); } 
	}

	public string primaryWindowLabel 
	{ 
		get => _primaryWindowLabel; 
		set { _primaryWindowLabel = value; OnPropertyChanged(); } 
	}

	public string primaryResetDescription 
	{ 
		get => _primaryResetDescription; 
		set { _primaryResetDescription = value; OnPropertyChanged(); } 
	}

	// Secondary Window (shows reset date)
	public int secondaryUsedPercent 
	{ 
		get => _secondaryUsedPercent; 
		set { _secondaryUsedPercent = value; OnPropertyChanged(); } 
	}

	public string secondaryWindowLabel 
	{ 
		get => _secondaryWindowLabel; 
		set {
            _secondaryWindowLabel = string.IsNullOrWhiteSpace(value) ? string.Empty : $"{value} ";
			OnPropertyChanged(); 
		} 
	}

	public string secondaryResetDate 
	{ 
		get => _secondaryResetDate; 
		set { _secondaryResetDate = value; OnPropertyChanged(); } 
	}

	// Anonymous Mode
	[JsonIgnore]
	public bool IsAnonymous
	{
		get => _isAnonymous;
		set 
		{ 
			_isAnonymous = value; 
			OnPropertyChanged(); 
			OnPropertyChanged(nameof(DisplayName));
			OnPropertyChanged(nameof(DisplayEmail));
		}
	}

	[JsonIgnore]
	public bool IsRefreshing
	{
		get => _isRefreshing;
		set { _isRefreshing = value; OnPropertyChanged(); }
	}

	[JsonIgnore]
	public bool IsRefreshQueued
	{
		get => _isRefreshQueued;
		set { _isRefreshQueued = value; OnPropertyChanged(); }
	}

	[JsonIgnore]
	public bool HasError
	{
		get => _hasError;
		set { _hasError = value; OnPropertyChanged(); }
	}

	[JsonIgnore]
	public int DisplayIndex { get; set; }

	[JsonIgnore]
	public string DisplayName => IsAnonymous ? $"User {DisplayIndex + 1}" : 
		(!string.IsNullOrEmpty(name) ? name : "Codex Account");

	[JsonIgnore]
	public string DisplayEmail => IsAnonymous ? login_method.ToUpper() : 
		(!string.IsNullOrEmpty(email) ? email : login_method.ToUpper());

	[JsonIgnore]
	public int PrimaryRemainingPercent => Math.Max(0, 100 - primaryUsedPercent);

	[JsonIgnore]
	public bool IsRateLimited => PrimaryRemainingPercent <= 1;

	[JsonIgnore]
	public string PlanDisplay => string.IsNullOrEmpty(plan_type) ? "—" : 
		System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(plan_type.Replace("_", " "));

	[JsonIgnore]
	public string CreditsDisplay => unlimited_credits ? "Unlimited" : 
		(has_credits ? $"${credits:F2}" : "—");

	public event PropertyChangedEventHandler? PropertyChanged;
	protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
