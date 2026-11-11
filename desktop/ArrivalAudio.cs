using System.Diagnostics;
using System.IO;
using System.Media;
using System.Speech.Synthesis;
using System.Windows.Media;

namespace Vigia;

internal sealed class ArrivalAudio : IDisposable
{
    private MediaPlayer? player;
    private SpeechSynthesizer? speech;
    private SoundPlayer? chime;
    private bool speechChecked;

    public void Play(int people)
    {
        try
        {
            player?.Close();
            speech?.SpeakAsyncCancelAll();
            string personalVoice = Path.Combine(AppContext.BaseDirectory, "voice-casa");
            string voiceFolder = Directory.Exists(personalVoice)
                ? personalVoice : Path.Combine(AppContext.BaseDirectory, "voice");
            string clip = Path.Combine(voiceFolder, $"{people}.mp3");
            if (File.Exists(clip))
            {
                if (player is null)
                {
                    player = new MediaPlayer();
                    player.MediaFailed += (_, _) => PlayChime();
                }
                player.Open(new Uri(clip));
                player.Play();
                return;
            }

            if (!speechChecked)
            {
                speechChecked = true;
                using var candidate = new SpeechSynthesizer();
                var voice = candidate.GetInstalledVoices().Where(voice => voice.Enabled)
                    .Select(voice => voice.VoiceInfo)
                    .Where(voice => voice.Culture.TwoLetterISOLanguageName == "pt")
                    .OrderByDescending(voice => voice.Culture.Name == "pt-BR").FirstOrDefault();
                if (voice is not null)
                {
                    speech = new SpeechSynthesizer();
                    speech.SelectVoice(voice.Name);
                }
            }
            if (speech is not null)
            {
                speech.SpeakAsync(people == 1 ? "Uma pessoa chegou." : $"{people} pessoas chegaram.");
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine(ex.Message);
        }
        PlayChime();
    }

    private void PlayChime()
    {
        try
        {
            chime ??= new SoundPlayer(typeof(ArrivalAudio).Assembly.GetManifestResourceStream("Vigia.Arrival.wav")
                ?? throw new InvalidOperationException("Som do Vigia indisponível."));
            chime.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    public void Dispose()
    {
        player?.Close();
        speech?.Dispose();
        chime?.Stop();
        chime?.Stream?.Dispose();
        chime?.Dispose();
    }
}
