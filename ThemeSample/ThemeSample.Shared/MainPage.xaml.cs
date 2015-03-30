using System;
using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Em.UI.Themes;

namespace ThemeSample
{
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<string> Themes { get; set; }

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            Themes = new ObservableCollection<string>();
            DataContext = this;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            var themes = await DisplayTheme.GetThemesAsync();
            Themes.Clear();
            foreach (var theme in themes)
            {
                Themes.Add(theme);
            }
        }

        private void ListViewBase_OnItemClick(object sender, ItemClickEventArgs e)
        {
            DisplayTheme.SetRequestedTheme(Window.Current.Content, (string) e.ClickedItem);
        }
    }
}