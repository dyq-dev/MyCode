using System.Windows.Controls;
using AI.Assistant.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Assistant.Client.Views;

public partial class KnowledgePlaygroundView : UserControl
{
    public KnowledgePlaygroundView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<KnowledgePlaygroundViewModel>();
    }
}
