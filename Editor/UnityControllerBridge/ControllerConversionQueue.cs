using System;
using System.Collections.Generic;
using DMotion.Authoring.UnityControllerBridge;
using UnityEditor;
using UnityEngine;

namespace DMotion.Editor.UnityControllerBridge
{
    /// <summary>
    /// Background queue processor for Unity Controller Bridge conversions.
    /// Processes conversion requests without blocking the editor.
    /// </summary>
    public static class ControllerConversionQueue
    {
        private static readonly Queue<ConversionRequest> _queue = new();
        private static readonly HashSet<string> _queuedBridgeIds = new();
        private static ConversionRequest _currentRequest;
        private static bool _isProcessing;

        #region Events

        /// <summary>
        /// Event fired when a conversion starts.
        /// </summary>
        public static event Action<string> OnConversionStarted;

        /// <summary>
        /// Event fired when a conversion finishes (success or failure).
        /// </summary>
        public static event Action<string, bool> OnConversionFinished; // bridgeId, success

        /// <summary>
        /// Event fired when all conversions are complete.
        /// </summary>
        public static event Action OnConversionComplete;

        /// <summary>
        /// Event fired during conversion for progress updates.
        /// </summary>
        public static event Action<float, string> OnProgressUpdated; // progress [0-1], message

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether the queue is currently processing.
        /// </summary>
        public static bool IsProcessing => _isProcessing;

        /// <summary>
        /// Number of items in the queue.
        /// </summary>
        public static int QueueCount => _queue.Count + (_currentRequest != null ? 1 : 0);

        #endregion

        #region Public API

        /// <summary>
        /// Enqueues a bridge for conversion.
        /// Duplicate requests are ignored (same bridge can't be queued twice).
        /// </summary>
        public static void Enqueue(UnityControllerBridgeAsset bridge)
        {
            if (bridge == null)
            {
                Debug.LogWarning("[ControllerConversionQueue] Cannot enqueue null bridge");
                return;
            }

            string bridgeId = bridge.BridgeId;

            // Check for duplicates
            if (_queuedBridgeIds.Contains(bridgeId))
            {
                Debug.Log($"[ControllerConversionQueue] Bridge '{bridgeId}' already queued, skipping duplicate");
                return;
            }

            // Check if currently processing this bridge
            if (_currentRequest != null && _currentRequest.BridgeId == bridgeId)
            {
                Debug.Log($"[ControllerConversionQueue] Bridge '{bridgeId}' currently processing, skipping");
                return;
            }

            var request = new ConversionRequest
            {
                Bridge = bridge,
                BridgeId = bridgeId,
                SourceController = bridge.SourceController,
                QueueTime = EditorApplication.timeSinceStartup
            };

            _queue.Enqueue(request);
            _queuedBridgeIds.Add(bridgeId);

            Debug.Log($"[ControllerConversionQueue] Enqueued bridge '{bridgeId}' (queue size: {_queue.Count})");
        }

        /// <summary>
        /// Starts processing the queue.
        /// </summary>
        public static void StartProcessing()
        {
            if (_isProcessing)
            {
                Debug.Log("[ControllerConversionQueue] Already processing");
                return;
            }

            if (_queue.Count == 0 && _currentRequest == null)
            {
                Debug.Log("[ControllerConversionQueue] Queue empty, nothing to process");
                return;
            }

            _isProcessing = true;
            EditorApplication.update += ProcessQueue;
            Debug.Log($"[ControllerConversionQueue] Started processing ({_queue.Count} items)");
        }

        /// <summary>
        /// Stops processing the queue.
        /// Current conversion will finish, but no more will start.
        /// </summary>
        public static void StopProcessing()
        {
            if (!_isProcessing)
            {
                return;
            }

            _isProcessing = false;
            EditorApplication.update -= ProcessQueue;
            Debug.Log("[ControllerConversionQueue] Stopped processing");
        }

        /// <summary>
        /// Clears the queue (does not stop current conversion).
        /// </summary>
        public static void Clear()
        {
            _queue.Clear();
            _queuedBridgeIds.Clear();
            Debug.Log("[ControllerConversionQueue] Queue cleared");
        }

        #endregion

        #region Private Methods

        private static void ProcessQueue()
        {
            // If we have a current request in progress, check if it's complete
            if (_currentRequest != null)
            {
                if (_currentRequest.IsComplete)
                {
                    FinalizeCurrentConversion();
                }
                return;
            }

            // Check if queue is empty
            if (_queue.Count == 0)
            {
                // All done
                _isProcessing = false;
                EditorApplication.update -= ProcessQueue;
                OnConversionComplete?.Invoke();
                Debug.Log("[ControllerConversionQueue] All conversions complete");
                return;
            }

            // Start next conversion
            _currentRequest = _queue.Dequeue();
            _queuedBridgeIds.Remove(_currentRequest.BridgeId);
            StartConversion(_currentRequest);
        }

        private static void StartConversion(ConversionRequest request)
        {
            OnConversionStarted?.Invoke(request.BridgeId);
            OnProgressUpdated?.Invoke(0f, $"Converting '{request.SourceController?.name ?? "unknown"}'...");

            Debug.Log($"[ControllerConversionQueue] Starting conversion: {request.BridgeId}");

            try
            {
                // Get output path
                var config = ControllerBridgeConfig.Instance;
                config.EnsureOutputDirectory();
                string outputPath = config.GetOutputPath(request.SourceController.name);

                // Perform actual conversion
                var stateMachine = UnityControllerConverter.ConvertController(
                    request.SourceController,
                    outputPath,
                    config
                );

                request.ResultStateMachine = stateMachine;
                request.IsComplete = true;
                request.Success = stateMachine != null;

                if (!request.Success)
                {
                    request.ErrorMessage = "Conversion returned null";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ControllerConversionQueue] Conversion failed: {ex.Message}\n{ex.StackTrace}");
                request.IsComplete = true;
                request.Success = false;
                request.ErrorMessage = ex.Message;
            }
        }

        private static void FinalizeCurrentConversion()
        {
            if (_currentRequest.Success)
            {
                // Update bridge with generated asset
                _currentRequest.Bridge.SetGeneratedStateMachine(_currentRequest.ResultStateMachine);
                _currentRequest.Bridge.MarkClean();
                EditorUtility.SetDirty(_currentRequest.Bridge);

                Debug.Log($"[ControllerConversionQueue] Conversion succeeded: {_currentRequest.BridgeId}");
            }
            else
            {
                Debug.LogError($"[ControllerConversionQueue] Conversion failed: {_currentRequest.BridgeId} - {_currentRequest.ErrorMessage}");
            }

            OnConversionFinished?.Invoke(_currentRequest.BridgeId, _currentRequest.Success);
            OnProgressUpdated?.Invoke(1f, _currentRequest.Success ? "Complete" : "Failed");

            _currentRequest = null;
        }

        #endregion

        #region Request Class

        private class ConversionRequest
        {
            public UnityControllerBridgeAsset Bridge;
            public string BridgeId;
            public UnityEditor.Animations.AnimatorController SourceController;
            public double QueueTime;

            // Result
            public Authoring.StateMachineAsset ResultStateMachine;
            public bool IsComplete;
            public bool Success;
            public string ErrorMessage;
        }

        #endregion
    }
}
