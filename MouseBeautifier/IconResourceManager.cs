using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;

namespace MouseBeautifier
{
    /// <summary>
    /// Owns Win2D icon resources. Asynchronous replacements are cancellable and
    /// generation-checked so an older load can never overwrite a newer setting.
    /// </summary>
    internal sealed class IconResourceManager : IDisposable
    {
        private readonly object _gate = new();
        private readonly ICanvasResourceCreator _creator;
        private readonly CancellationTokenSource _lifetime = new();
        private CancellationTokenSource? _customLoad;
        private IconImage? _custom;
        private IconImage? _pig;
        private IconImage? _girl;
        private long _bundleGeneration;
        private long _customGeneration;
        private bool _disposed;

        public IconResourceManager(ICanvasResourceCreator creator)
        {
            _creator = creator ??
                throw new ArgumentNullException(nameof(creator));
        }

        public async Task InitializeAsync(
            string baseDirectory)
        {
            long generation;
            lock (_gate)
            {
                ThrowIfDisposed();
                generation = ++_bundleGeneration;
            }

            CancellationToken token = _lifetime.Token;
            Task<IconImage?> pigTask = IconImage.LoadAsync(
                _creator,
                Path.Combine(baseDirectory, "Assets", "pig.png"),
                token);
            Task<IconImage?> girlTask = IconImage.LoadAsync(
                _creator,
                Path.Combine(baseDirectory, "Assets", "girl.png"),
                token);

            IconImage? pig = null;
            IconImage? girl = null;
            try
            {
                await Task.WhenAll(pigTask, girlTask);
                pig = await pigTask;
                girl = await girlTask;
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                pig?.Dispose();
                girl?.Dispose();
                return;
            }

            IconImage? oldPig = null;
            IconImage? oldGirl = null;
            bool accepted = false;
            lock (_gate)
            {
                if (_disposed || generation != _bundleGeneration)
                {
                    pig?.Dispose();
                    girl?.Dispose();
                }
                else
                {
                    oldPig = _pig;
                    oldGirl = _girl;
                    _pig = pig;
                    _girl = girl;
                    accepted = true;
                }
            }

            oldPig?.Dispose();
            oldGirl?.Dispose();
            if (!accepted)
            {
                return;
            }
        }

        public async Task ReloadCustomAsync(string? path)
        {
            CancellationTokenSource load;
            CancellationToken token;
            long generation;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _customLoad?.Cancel();
                _customLoad?.Dispose();
                _customLoad = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetime.Token);
                load = _customLoad;
                token = load.Token;
                generation = ++_customGeneration;
            }

            IconImage? icon = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    !string.Equals(
                        Path.GetExtension(path),
                        ".svg",
                        StringComparison.OrdinalIgnoreCase))
                {
                    icon = await IconImage.LoadAsync(
                        _creator,
                        path,
                        token);
                }

                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                icon?.Dispose();
                return;
            }

            IconImage? old = null;
            lock (_gate)
            {
                if (_disposed ||
                    generation != _customGeneration ||
                    token.IsCancellationRequested)
                {
                    icon?.Dispose();
                }
                else
                {
                    old = _custom;
                    _custom = icon;
                }
            }

            old?.Dispose();
        }

        public IconImage? Get(string iconType)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return null;
                }

                return iconType switch
                {
                    "custom" => _custom,
                    "pig" => _pig,
                    "girl" => _girl,
                    _ => null,
                };
            }
        }

        public void Dispose()
        {
            IconImage? custom;
            IconImage? pig;
            IconImage? girl;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _bundleGeneration++;
                _customGeneration++;
                _lifetime.Cancel();
                _customLoad?.Cancel();

                custom = _custom;
                pig = _pig;
                girl = _girl;
                _custom = null;
                _pig = null;
                _girl = null;
            }

            custom?.Dispose();
            pig?.Dispose();
            girl?.Dispose();
            _customLoad?.Dispose();
            _lifetime.Dispose();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);
        }
    }
}
