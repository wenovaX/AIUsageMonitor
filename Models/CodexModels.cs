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
	private string _secondaryResetDescription = string.Empty;

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
	
	[JsonIgnore]
	public string access_token 
	{ 
		get => _accessToken; 
		set { _accessToken = value; OnPropertyChanged(); } 
	}

	[JsonIgnore]
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
		set
		{
			_primaryUsedPercent = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PrimaryRemainingPercent));
			OnPropertyChanged(nameof(EffectiveRemainingPercent));
			OnPropertyChanged(nameof(IsRateLimited));
			OnPropertyChanged(nameof(PrimaryStatusText));
		}
	}

	public string primaryWindowLabel 
	{ 
		get => _primaryWindowLabel; 
		set
		{
			_primaryWindowLabel = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(UsageLimitBadgeText));
			OnPropertyChanged(nameof(HasUsageLimitBadge));
		}
	}

	public string primaryResetDescription 
	{ 
		get => _primaryResetDescription; 
		set
		{
			_primaryResetDescription = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PrimaryStatusText));
		}
	}

	// Secondary Window (shows reset date)
	public int secondaryUsedPercent 
	{ 
		get => _secondaryUsedPercent; 
		set
		{
			_secondaryUsedPercent = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsWeeklyBlocked));
			OnPropertyChanged(nameof(EffectiveRemainingPercent));
			OnPropertyChanged(nameof(IsRateLimited));
			OnPropertyChanged(nameof(PrimaryStatusText));
		}
	}

	public string secondaryWindowLabel 
	{ 
		get => _secondaryWindowLabel; 
		set
		{
            _secondaryWindowLabel = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
			OnPropertyChanged();
			OnPropertyChanged(nameof(SecondaryResetHeader));
			OnPropertyChanged(nameof(IsWeeklyBlocked));
			OnPropertyChanged(nameof(EffectiveRemainingPercent));
			OnPropertyChanged(nameof(IsRateLimited));
			OnPropertyChanged(nameof(PrimaryStatusText));
			OnPropertyChanged(nameof(SecondaryStatusText));
			OnPropertyChanged(nameof(HasSecondaryWindow));
			OnPropertyChanged(nameof(SecondaryRemainingPercent));
		}
	}

	public string secondaryResetDate 
	{ 
		get => _secondaryResetDate; 
		set
		{
			_secondaryResetDate = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PrimaryStatusText));
		}
	}

	public string secondaryResetDescription
	{
		get => _secondaryResetDescription;
		set
		{
			_secondaryResetDescription = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PrimaryStatusText));
		}
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

	private string _lastErrorMessage = "";
	[JsonIgnore]
	public string LastErrorMessage
	{
		get => _lastErrorMessage;
		set { _lastErrorMessage = value; OnPropertyChanged(); }
	}

	private string _promoMessage = "";
	[JsonIgnore]
	public string PromoMessage
	{
		get => _promoMessage;
		set { _promoMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPromoMessage)); }
	}

	[JsonIgnore]
	public bool HasPromoMessage => !string.IsNullOrWhiteSpace(PromoMessage);

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
	public int EffectiveRemainingPercent => PrimaryRemainingPercent; // Show true usage even if weekly blocked

	[JsonIgnore]
	public int SecondaryRemainingPercent => Math.Max(0, 100 - secondaryUsedPercent);

	[JsonIgnore]
	public bool HasSecondaryWindow => !string.IsNullOrWhiteSpace(secondaryWindowLabel);

	[JsonIgnore]
	public bool IsPrimaryWindowExhausted => PrimaryRemainingPercent <= 1;

	[JsonIgnore]
	public bool IsWeeklyBlocked =>
		string.Equals(secondaryWindowLabel, "Weekly", StringComparison.OrdinalIgnoreCase) &&
		secondaryUsedPercent >= 100;

	[JsonIgnore]
	public string PrimaryStatusText => $"Resets: {primaryResetDescription}";

	[JsonIgnore]
	public string SecondaryStatusText => $"Resets: {secondaryResetDescription}";

	[JsonIgnore]
	public string SecondaryResetHeader => string.IsNullOrWhiteSpace(secondaryWindowLabel)
		? string.Empty
		: $"{secondaryWindowLabel} RESET";

	[JsonIgnore]
	public string UsageLimitBadgeText
	{
		get
		{
			if (IsWeeklyBlocked && IsPrimaryWindowExhausted)
				return "WEEKLY & 5H BLOCKED";
			if (IsWeeklyBlocked)
				return "WEEKLY BLOCKED";
			if (IsPrimaryWindowExhausted)
				return $"{primaryWindowLabel.ToUpperInvariant()} BLOCKED";
			return string.Empty;
		}
	}

	[JsonIgnore]
	public bool HasUsageLimitBadge => !string.IsNullOrWhiteSpace(UsageLimitBadgeText);

	[JsonIgnore]
	public bool IsRateLimited => IsWeeklyBlocked || IsPrimaryWindowExhausted;

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

public class CodexAccountRow
{
	public System.Collections.ObjectModel.ObservableCollection<CodexAccount> Accounts { get; } = new();
}
