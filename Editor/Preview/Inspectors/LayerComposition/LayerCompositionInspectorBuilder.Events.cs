namespace DMotion.Editor
{
    internal partial class LayerCompositionInspectorBuilder
    {
        #region Private - Composition State Events
        
        private void SubscribeToCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged += OnCompositionStatePropertyChanged;
            compositionState.LayerChanged += OnCompositionLayerChanged;
            LogDebug($"Subscribed to CompositionState events (LayerCount={compositionState.LayerCount})");
        }
        
        private void UnsubscribeFromCompositionState()
        {
            if (compositionState == null) return;
            
            compositionState.PropertyChanged -= OnCompositionStatePropertyChanged;
            compositionState.LayerChanged -= OnCompositionLayerChanged;
        }
        
        private void OnCompositionStatePropertyChanged(object sender, ObservablePropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ObservableCompositionState.MasterTime):
                    globalTimeSlider?.SetValueWithoutNotify(compositionState.MasterTime);
                    break;
                    
                case nameof(ObservableCompositionState.IsPlaying):
                    isPlaying = compositionState.IsPlaying;
                    UpdatePlayButton();
                    break;
                    
                case nameof(ObservableCompositionState.SyncLayers):
                    syncLayersToggle?.SetValueWithoutNotify(compositionState.SyncLayers);
                    break;
            }
        }
        
        private void OnCompositionLayerChanged(object sender, LayerPropertyChangedEventArgs e)
        {
            // Skip refresh if we're the source of the change (prevents recursive cascade)
            if (_suppressLayerChangeEvents)
            {
                LogTrace($"OnCompositionLayerChanged: Suppressed (Property={e.PropertyName}, Layer={e.LayerIndex})");
                return;
            }
            
            LogTrace($"OnCompositionLayerChanged: Property={e.PropertyName}, Layer={e.LayerIndex}");
            
            var layerIndex = e.LayerIndex;
            if (layerIndex >= 0 && layerIndex < layerSections.Count)
            {
                RefreshLayerSection(layerSections[layerIndex]);
            }
        }
        
        #endregion
        
        #region Logging
        
        private void LogTrace(string message) => _logger?.Trace(message);
        private void LogDebug(string message) => _logger?.Debug(message);
        private void LogInfo(string message) => _logger?.Info(message);
        private void LogWarning(string message) => _logger?.Warning(message);
        private void LogError(string message) => _logger?.Error(message);
        
        #endregion
    }
}
