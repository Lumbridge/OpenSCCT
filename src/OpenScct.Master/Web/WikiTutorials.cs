namespace OpenScct.Master.Web;

/// <summary>Browser training markup, kept separate from the long-form guide catalog.</summary>
internal static class WikiTutorials
{
    internal static string VisionCancelling => $$"""
        <link rel="stylesheet" href="{{WebHtml.AssetUrl("/web/assets/vision-cancelling.css")}}">
        <div class="training" id="vision-training" data-mode="guided">
          <div class="training-topline"><span class="training-label"><span aria-hidden="true">◉</span> INTERACTIVE TRAINING / 01</span><span class="training-local">No install · No sign-in</span></div>
          <div class="training-layout">
            <div class="training-main">
              <div class="training-stage" data-vc="stage" aria-label="Vision cancelling training scene">
                <div class="training-hud"><span class="training-status" data-vc="phase">LOADING TRAINING ROOM</span><span class="training-vision" data-vc="vision">NORMAL VISION</span></div>
                <div class="training-reticle" aria-hidden="true">+</div>
                <div class="training-cue" data-vc="cue" aria-hidden="true"><strong>TOGGLE VISION</strong><span>Touch down. Keep moving.</span></div>
                <div class="training-stage-note" data-vc="stage-note">Loading the spy and original animations…</div>
                <div class="training-flash" aria-hidden="true"></div>
                <p class="training-error" data-vc="error" role="status"></p>
                <noscript><p class="training-error">Enable JavaScript to play. The steps below explain the technique.</p></noscript>
              </div>
              <div class="training-timeline" data-vc="timeline" aria-label="Landing timing feedback">
                <div class="training-timeline-label"><span>APPROACH</span><span>TOUCHDOWN</span><span>RECOVERY</span></div>
                <div class="training-track"><span class="training-window"></span><span class="training-contact"></span><span class="training-playhead" data-vc="playhead"></span><span class="training-input" data-vc="input-marker" hidden></span></div>
                <p data-vc="timing-note">The highlighted area is your practice window. Tap once as your feet touch the floor.</p>
              </div>
              <div class="training-controls">
                <button type="button" class="button" data-vc="drop" disabled>Start drop <kbd>Space</kbd></button>
                <button type="button" class="button secondary" data-vc="night" disabled>Night vision <kbd>1</kbd></button>
                <button type="button" class="button secondary" data-vc="thermal" disabled>Thermal <kbd>2</kbd></button>
                <button type="button" class="training-icon-button" data-vc="pause" disabled aria-label="Pause training">Pause</button>
              </div>
              <div class="training-keyboard"><span><kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd> move</span><span><kbd>V</kbd> switch vision</span><span><kbd>R</kbd> retry</span><span>Click the scene to use the keyboard</span></div>
              <div class="training-touch" aria-label="Touch movement controls"><button type="button" data-move="left" aria-label="Move left">←</button><button type="button" data-move="forward" aria-label="Move forward">↑</button><button type="button" data-move="back" aria-label="Move backward">↓</button><button type="button" data-move="right" aria-label="Move right">→</button></div>
            </div>
            <aside class="training-coach" aria-label="Training coach">
              <p class="training-label">THE TECHNIQUE</p><h3>Land.<br>Switch.<br><span>Keep moving.</span></h3>
              <p>A high drop normally stops a spy in a landing stagger. Switch vision as you land to cancel that recovery.</p>
              <div class="training-modes" role="group" aria-label="Training mode"><button type="button" data-mode="demo" aria-pressed="false">Watch</button><button type="button" data-mode="guided" aria-pressed="true">Guided</button><button type="button" data-mode="challenge" aria-pressed="false">Challenge</button></div>
              <p class="training-mode-help" data-vc="mode-help">Follow the landing cue, then try it without the hints.</p>
              <div class="training-result" data-vc="result" role="status" aria-live="polite" aria-atomic="true"><span class="training-label" data-vc="result-label">YOUR NEXT MOVE</span><strong data-vc="verdict">Step off the ledge</strong><p data-vc="detail">Press Space to drop, or hold W to walk off. Tap 1, 2 or V as you land.</p></div>
              <div class="training-stats"><div><strong data-vc="successes">0 / 0</strong><span>successful cancels</span></div><div><strong data-vc="streak">0</strong><span>current streak</span></div></div>
              <label class="training-setting">Drop height<select data-vc="height"><option value="4.6">4.6 m · Lower ledge</option><option value="5.2" selected>5.2 m · Standard ledge</option><option value="5.8">5.8 m · Higher ledge</option></select></label>
              <label class="training-slow"><input type="checkbox" data-vc="slow">Slow motion <span>0.6×</span></label>
              <button type="button" class="training-reset" data-vc="reset-stats">Reset practice stats</button>
            </aside>
          </div>
          <p class="training-footnote">Browser timing exercise · Original spy model and animation clips · Simplified movement and vision effects. The practice window is a teaching aid, not a measured game timing guarantee.</p>
        </div>
        <script type="module" src="{{WebHtml.AssetUrl("/web/assets/vision-cancelling.js")}}"></script>
        """;
}
