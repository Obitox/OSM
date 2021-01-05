using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OSMTradeHistory
{
    public sealed class MainViewModel : INotifyPropertyChanged
        {
            private string _searchCriteria;
            
            public string SearchCriteria
            {
                get => _searchCriteria;
                set
                {
                    if (_searchCriteria == value) return;
                    _searchCriteria = value.ToUpper();
                    OnPropertyChanged(nameof(SearchCriteria));
                }
            }
    
            public event PropertyChangedEventHandler PropertyChanged;
    
            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
}