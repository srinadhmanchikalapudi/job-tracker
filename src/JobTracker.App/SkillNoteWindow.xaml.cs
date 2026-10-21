using System.Windows;

namespace JobTracker.App;

/// <summary>Asks, in one box, how the person used a skill the resume does not mention. Their words are the only thing a rewrite may say about it.</summary>
public partial class SkillNoteWindow : Window
{
    public SkillNoteWindow(string skill)
    {
        InitializeComponent();
        Heading.Text = $"I've used {skill}";
        Title = Heading.Text;
    }

    public string Note => NoteBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
