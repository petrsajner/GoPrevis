using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClipboardKey = GoPrevis.PropertyKeyClipboard.Key;

namespace GoPrevis;

public partial class GoPrevisApp
{
    Control? _animationWorkspace, _animationLegacyPanel, _animationDopePanel, _animationGraphPanel;
    AnimationTrackCanvas? _animationDope, _animationHierarchy;
    PropertyGraphEditor? _animationGraph;
    LineEdit? _animationSearch;
    CheckButton? _animationAnimatedOnly, _animationSelectedOnly, _animationPinnedOnly;
    OptionButton? _animationGraphKind, _animationPreset;
    Button? _animationFit, _animationExpandButton, _animationKeyButton;
    HBoxContainer? _animationToolbarMain, _animationToolbarSecondary;
    Control? _animationToolbarSpacer, _animationKeyControls;
    SpinBox? _animationSelectedTime, _animationSelectedValue, _animationSelectedSpeed;
    Label? _animationSelectedKeyInfo;
    bool _refreshingAnimationKeyFields;
    int _animationToolbarArrangement=-1;
    readonly Dictionary<string,Button> _animationModes=new();
    readonly HashSet<string> _animationExpanded=new(StringComparer.Ordinal);
    readonly HashSet<string> _animationSelectedChannels=new(StringComparer.Ordinal);
    readonly HashSet<string> _animationPinnedChannels=new(StringComparer.Ordinal);
    readonly HashSet<string> _animationSeenTargets=new(StringComparer.Ordinal);
    readonly Dictionary<string,AnimationChannelBinding> _animationChannelMap=new(StringComparer.Ordinal);
    List<AnimationChannelBinding> _animationAllChannels=new();
    List<AnimationWorkspaceRow> _animationRows=new();
    string _animationWorkspaceMode="Dope sheet";
    bool _refreshingAnimationWorkspace;
    int _animationStructureHash;

    sealed class AnimationWorkspaceRow
    {
        public string Key="",Label="",TargetId="";
        public int Depth;
        public bool IsGroup,Expanded;
        public List<string> Channels=new();
    }
    sealed class AnimationHierarchyNode
    {
        public string Key="",Label="",TargetId="";
        public List<string> Channels=new();
        public List<AnimationHierarchyNode> Children=new();
        public AnimationChannelBinding? Binding;
    }

    internal List<AnimationChannelBinding> ActiveAnimationChannels => _animationRows
        .SelectMany(row=>row.Channels).Distinct().Where(_animationSelectedChannels.Contains)
        .Where(_animationChannelMap.ContainsKey).Select(id=>_animationChannelMap[id]).ToList();
    internal int AnimationWorkspaceVisibleRowCount => _animationRows.Count;

    void InstallAnimationWorkspace(VBoxContainer timelinePane,Control existingTracksPanel)
    {
        timelinePane.RemoveChild(existingTracksPanel);
        var workspace=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};
        workspace.AddThemeConstantOverride("separation",1);_animationWorkspace=workspace;
        var toolbar=new HBoxContainer();_animationToolbarMain=toolbar;toolbar.AddThemeConstantOverride("separation",5);
        foreach(string mode in new[]{"Dope sheet","Graph","Clips"})
        {
            string captured=mode;var button=Button(mode,()=>SetAnimationWorkspaceMode(captured),true);
            button.AddThemeFontSizeOverride("font_size",11);_animationModes[mode]=button;toolbar.AddChild(button);
        }
        _animationSearch=new LineEdit{PlaceholderText="Find animated properties…",ClearButtonEnabled=true,CustomMinimumSize=new Vector2(130,0),SizeFlagsHorizontal=SizeFlags.ExpandFill};
        _animationSearch.TextChanged+=_=>RefreshAnimationWorkspace();toolbar.AddChild(_animationSearch);
        _animationAnimatedOnly=new CheckButton{Text="Animated only"};_animationSelectedOnly=new CheckButton{Text="Selected only"};_animationPinnedOnly=new CheckButton{Text="Pinned"};
        _animationAnimatedOnly.TooltipText="Show only channels that already contain keys.";
        _animationSelectedOnly.TooltipText="Show the selected scene object's properties. Pinned channels remain available.";
        _animationPinnedOnly.TooltipText="Show only starred channels. Click a property's star to pin or unpin it.";
        foreach(var filter in new[]{_animationAnimatedOnly,_animationSelectedOnly,_animationPinnedOnly}){filter.AddThemeFontSizeOverride("font_size",11);filter.Toggled+=_=>RefreshAnimationWorkspace();toolbar.AddChild(filter);}
        var expand=Button("Expand",ShowAnimationExpandMenu);_animationExpandButton=expand;expand.TooltipText="Expand all properties, expand animated properties, or collapse the hierarchy.";toolbar.AddChild(expand);
        var key=Button("◇ Key",()=>KeyAnimationChannels(ActiveAnimationChannels));_animationKeyButton=key;key.TooltipText="Key selected properties at the playhead (K). Each XYZ axis is an independent channel.";toolbar.AddChild(key);
        _animationGraphKind=new OptionButton();_animationGraphKind.AddItem("Value graph");_animationGraphKind.AddItem("Speed graph");_animationGraphKind.ItemSelected+=i=>{if(_animationGraph!=null){_animationGraph.SpeedMode=i==1;_animationGraph.FitCurves();}};toolbar.AddChild(_animationGraphKind);
        _animationPreset=new OptionButton{TooltipText="Apply interpolation to selected keys"};_animationPreset.AddItem("Interpolation…");
        foreach(string preset in new[]{"Linear","Bezier","Hold","Ease in/out","Stop","Auto","Flat"})_animationPreset.AddItem(preset);
        _animationPreset.ItemSelected+=i=>{if(i<=0)return;string preset=_animationPreset.GetItemText((int)i);if(_animationWorkspaceMode=="Graph")_animationGraph?.SetPreset(preset);else _animationDope?.ApplySelectedPreset(preset);_animationPreset.Select(0);};toolbar.AddChild(_animationPreset);
        _animationFit=Button("Fit",()=>{if(_animationWorkspaceMode=="Graph")_animationGraph?.FitCurves();else _animationDope?.FitTime();});toolbar.AddChild(_animationFit);
        var toolbarRows=new VBoxContainer();toolbarRows.AddThemeConstantOverride("separation",1);toolbarRows.AddChild(toolbar);
        _animationToolbarSecondary=new HBoxContainer{Visible=false};_animationToolbarSecondary.AddThemeConstantOverride("separation",5);toolbarRows.AddChild(_animationToolbarSecondary);
        _animationToolbarSpacer=new Control{SizeFlagsHorizontal=SizeFlags.ExpandFill};_animationToolbarSecondary.AddChild(_animationToolbarSpacer);
        workspace.AddChild(CompactPanel(toolbarRows,5,1));
        _animationLegacyPanel=existingTracksPanel;workspace.AddChild(existingTracksPanel);
        _animationDope=new AnimationTrackCanvas{App=this,SizeFlagsHorizontal=SizeFlags.ExpandFill};
        var dopeScroll=new ScrollContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};dopeScroll.AddChild(_animationDope);
        _animationDopePanel=CompactPanel(dopeScroll,2,2);_animationDopePanel.SizeFlagsVertical=SizeFlags.ExpandFill;workspace.AddChild(_animationDopePanel);
        var graphSplit=new HSplitContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,SplitOffsets=new[]{250}};
        _animationHierarchy=new AnimationTrackCanvas{App=this,HierarchyOnly=true,CustomMinimumSize=new Vector2(215,0),SizeFlagsHorizontal=SizeFlags.ExpandFill};
        var hierarchyScroll=new ScrollContainer{CustomMinimumSize=new Vector2(215,0),SizeFlagsHorizontal=SizeFlags.Fill,SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};hierarchyScroll.AddChild(_animationHierarchy);graphSplit.AddChild(hierarchyScroll);
        _animationGraph=new PropertyGraphEditor{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(280,100)};
        _animationGraph.BeginEdit=()=>{StopHistoryPlayback();PushUndo();};_animationGraph.Changed=AnimationWorkspaceChanged;_animationGraph.Scrub=t=>{StopHistoryPlayback();SetTime(t);};graphSplit.AddChild(_animationGraph);
        _animationGraph.SelectionChanged+=RefreshAnimationKeyFields;
        _animationKeyControls=BuildAnimationKeyControls();workspace.AddChild(_animationKeyControls);workspace.MoveChild(_animationKeyControls,1);
        _animationGraphPanel=CompactPanel(graphSplit,2,2);_animationGraphPanel.SizeFlagsVertical=SizeFlags.ExpandFill;workspace.AddChild(_animationGraphPanel);
        workspace.Resized+=()=>ApplyAnimationToolbarLayout();
        timelinePane.AddChild(workspace);SetAnimationWorkspaceMode("Dope sheet");
    }

    Control BuildAnimationKeyControls()
    {
        _animationSelectedKeyInfo=Text("Select a graph key",10,true);_animationSelectedKeyInfo.CustomMinimumSize=new Vector2(150,0);_animationSelectedKeyInfo.ClipText=true;
        _animationSelectedTime=Spin(0,MaxSceneDuration,1.0/Math.Max(1,_fps),0);_animationSelectedTime.CustomMinimumSize=new Vector2(88,0);
        _animationSelectedValue=Spin(-1e9,1e9,.001,0);_animationSelectedValue.CustomMinimumSize=new Vector2(98,0);
        _animationSelectedSpeed=Spin(-1e9,1e9,.001,0);_animationSelectedSpeed.CustomMinimumSize=new Vector2(98,0);
        _animationSelectedTime.TooltipText="Time of the first selected key. Changing it retimes the selection while preserving spacing.";
        _animationSelectedValue.TooltipText="Value at the selected key. Changing it adjusts the selected value keys.";
        _animationSelectedSpeed.TooltipText="Signed rate at the selected key in parameter units per second. Editing it shapes the neighbouring curve tangents.";
        _animationSelectedTime.ValueChanged+=v=>{if(!_refreshingAnimationKeyFields)_animationGraph?.SetSelectedTime(v);};
        _animationSelectedValue.ValueChanged+=v=>{if(!_refreshingAnimationKeyFields)_animationGraph?.SetSelectedValue(v);};
        _animationSelectedSpeed.ValueChanged+=v=>{if(!_refreshingAnimationKeyFields)_animationGraph?.SetSelectedSpeed(v);};
        var row=Row(_animationSelectedKeyInfo,Text("Time",10,true),_animationSelectedTime,Text("s",10,true),Text("Value",10,true),_animationSelectedValue,Text("Speed",10,true),_animationSelectedSpeed,Text("units/s",10,true),Space());row.AddThemeConstantOverride("separation",5);
        return CompactPanel(row,5,1);
    }
    void RefreshAnimationKeyFields()
    {
        if(_animationGraph==null||_animationSelectedTime==null||_animationSelectedValue==null||_animationSelectedSpeed==null||_animationSelectedKeyInfo==null)return;
        int count=_animationGraph.SelectedKeyCount;_animationSelectedKeyInfo.Text=count==0?"Select a graph key":count==1?"Selected key":count+" selected keys";
        _refreshingAnimationKeyFields=true;
        try
        {
            _animationSelectedTime.Step=1.0/Math.Max(1,_fps);_animationSelectedTime.MaxValue=_duration;
            _animationSelectedTime.Editable=_animationSelectedValue.Editable=_animationSelectedSpeed.Editable=count>0;
            _animationSelectedTime.Value=_animationGraph.SelectedTime??0;
            _animationSelectedValue.Value=_animationGraph.SelectedValue??0;
            _animationSelectedSpeed.Value=_animationGraph.SelectedSpeed??0;
        }
        finally{_refreshingAnimationKeyFields=false;}
    }
    void ApplyAnimationToolbarLayout(bool force=false)
    {
        if(_animationWorkspace==null||_animationToolbarMain==null||_animationToolbarSecondary==null||_animationToolbarSpacer==null||_animationSearch==null||_animationExpandButton==null||_animationKeyButton==null||_animationAnimatedOnly==null||_animationSelectedOnly==null||_animationPinnedOnly==null||_animationGraphKind==null||_animationPreset==null||_animationFit==null)return;
        float width=_animationWorkspace.Size.X>1?_animationWorkspace.Size.X:GetViewportRect().Size.X;int arrangement=width<1450?0:1;
        if(!force&&arrangement==_animationToolbarArrangement)return;_animationToolbarArrangement=arrangement;
        Control[] modes={_animationModes["Dope sheet"],_animationModes["Graph"],_animationModes["Clips"]};
        Control[] primary=arrangement==0?modes.Concat(new Control[]{_animationSearch,_animationExpandButton}).ToArray():modes.Concat(new Control[]{_animationSearch,_animationAnimatedOnly,_animationSelectedOnly,_animationPinnedOnly,_animationExpandButton,_animationKeyButton,_animationGraphKind,_animationPreset,_animationFit}).ToArray();
        Control[] secondary=arrangement==0?new Control[]{_animationAnimatedOnly,_animationSelectedOnly,_animationPinnedOnly,_animationToolbarSpacer,_animationKeyButton,_animationGraphKind,_animationPreset,_animationFit}:new Control[]{_animationToolbarSpacer};
        void Place(HBoxContainer target,Control[] controls)
        {for(int i=0;i<controls.Length;i++){if(controls[i].GetParent()!=target)controls[i].Reparent(target,false);target.MoveChild(controls[i],i);}}
        Place(_animationToolbarMain,primary);Place(_animationToolbarSecondary,secondary);
        _animationToolbarSecondary.Visible=arrangement==0&&_animationWorkspaceMode!="Clips";
    }

    internal void SetAnimationWorkspaceMode(string mode)
    {
        if(!_animationModes.ContainsKey(mode))return;_animationWorkspaceMode=mode;
        foreach(var pair in _animationModes)pair.Value.ButtonPressed=pair.Key==mode;
        if(_animationLegacyPanel!=null)_animationLegacyPanel.Visible=mode=="Clips";
        if(_animationDopePanel!=null)_animationDopePanel.Visible=mode=="Dope sheet";
        if(_animationGraphPanel!=null)_animationGraphPanel.Visible=mode=="Graph";
        if(_animationKeyControls!=null)_animationKeyControls.Visible=mode=="Graph";
        if(_animationGraphKind!=null)_animationGraphKind.Visible=mode=="Graph";
        foreach(var control in new Control?[]{_animationSearch,_animationAnimatedOnly,_animationSelectedOnly,_animationPinnedOnly,_animationExpandButton,_animationKeyButton})if(control!=null)control.Visible=mode!="Clips";
        if(_animationPreset!=null)_animationPreset.Visible=mode!="Clips";
        if(_animationFit!=null)_animationFit.Visible=mode!="Clips";
        ApplyAnimationToolbarLayout(true);
        RefreshAnimationWorkspace();
    }

    PopupMenu? _animationExpandMenu;
    void ShowAnimationExpandMenu()
    {
        if(_animationExpandMenu==null)
        {
            _animationExpandMenu=new PopupMenu();AddChild(_animationExpandMenu);
            foreach(string label in new[]{"Expand all properties","Expand animated properties","Collapse all"})_animationExpandMenu.AddItem(label);
            _animationExpandMenu.IdPressed+=id=>
            {
                if(id==2)_animationExpanded.Clear();
                else {if(id==1)_animationAnimatedOnly!.ButtonPressed=true;foreach(var channel in _animationAllChannels.Where(c=>id==0||AnimationHasKeys(c)))ExpandAnimationChannel(channel);}
                _animationStructureHash=0;RefreshAnimationWorkspace();
            };
        }
        _animationExpandMenu.Position=(Vector2I)GetViewport().GetMousePosition()+GetWindow().Position;_animationExpandMenu.Popup();
    }

    static bool AnimationHasKeys(AnimationChannelBinding binding)=>binding.Curve()?.Keys.Count>0;
    IEnumerable<AnimationChannelBinding> AnimationRowChannels(AnimationWorkspaceRow row)=>row.Channels.Where(_animationChannelMap.ContainsKey).Select(id=>_animationChannelMap[id]);
    void ExpandAnimationChannel(AnimationChannelBinding channel)
    {
        string prefix="target:"+channel.TargetId;_animationExpanded.Add(prefix);
        foreach(string segment in channel.Group.Split(new[]{'/','.'},StringSplitOptions.RemoveEmptyEntries)){prefix+="/"+segment;_animationExpanded.Add(prefix);}
    }
    void SelectAnimationChannels(AnimationWorkspaceRow row,bool add)
    {
        var ids=row.Channels;bool remove=add&&ids.Count>0&&ids.All(_animationSelectedChannels.Contains);
        if(!add)_animationSelectedChannels.Clear();
        foreach(string id in ids){if(remove)_animationSelectedChannels.Remove(id);else _animationSelectedChannels.Add(id);}
        var entry=_entries.FirstOrDefault(e=>e.Data.Id==row.TargetId);var camera=_cameras.FirstOrDefault(c=>c.Id==row.TargetId);
        if(entry!=null&&_selected!=entry)Select(entry);else if(camera!=null&&_selectedCamera!=camera)SelectCamera(camera);
        RefreshAnimationWorkspace();
    }

    void RefreshAnimationWorkspace()
    {
        if(_animationWorkspace==null||_refreshingAnimationWorkspace)return;
        _refreshingAnimationWorkspace=true;
        try
        {
            _animationAllChannels=AnimationBindings();_animationChannelMap.Clear();foreach(var binding in _animationAllChannels)_animationChannelMap[binding.Identity]=binding;
            string selectedId=_selected?.Data.Id??_selectedCamera?.Id??"";
            if(selectedId!=""&&_animationSeenTargets.Add(selectedId))_animationExpanded.Add("target:"+selectedId);
            string search=_animationSearch?.Text.Trim()??"";var terms=search.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            var filtered=_animationAllChannels.Where(channel=>
                (_animationSelectedOnly?.ButtonPressed!=true||channel.TargetId==selectedId||_animationPinnedChannels.Contains(channel.Identity))&&
                (_animationAnimatedOnly?.ButtonPressed!=true||AnimationHasKeys(channel))&&
                (_animationPinnedOnly?.ButtonPressed!=true||_animationPinnedChannels.Contains(channel.Identity))&&
                terms.All(term=>(channel.TargetName+" "+channel.Group+" "+channel.Label+" "+channel.Path).Contains(term,StringComparison.OrdinalIgnoreCase))).ToList();
            var hash=new HashCode();foreach(var c in filtered){hash.Add(c.Identity);hash.Add(c.TargetName);hash.Add(c.Group);hash.Add(c.Label);hash.Add(AnimationHasKeys(c));}foreach(string value in _animationExpanded)hash.Add(value);hash.Add(search);int nextHash=hash.ToHashCode();
            if(nextHash!=_animationStructureHash)
            {
                _animationStructureHash=nextHash;var roots=new List<AnimationHierarchyNode>();
                foreach(var channel in filtered)
                {
                    string prefix="target:"+channel.TargetId;var root=roots.FirstOrDefault(n=>n.Key==prefix);
                    if(root==null){root=new(){Key=prefix,Label=channel.TargetName,TargetId=channel.TargetId};roots.Add(root);}root.Channels.Add(channel.Identity);var node=root;
                    foreach(string segment in channel.Group.Split(new[]{'/','.'},StringSplitOptions.RemoveEmptyEntries))
                    {
                        prefix+="/"+segment;var group=node.Children.FirstOrDefault(n=>n.Key==prefix);
                        if(group==null){group=new(){Key=prefix,Label=segment,TargetId=channel.TargetId};node.Children.Add(group);}group.Channels.Add(channel.Identity);node=group;
                    }
                    node.Children.Add(new(){Key="channel:"+channel.Identity,Label=channel.Label,TargetId=channel.TargetId,Binding=channel,Channels=new(){channel.Identity}});
                }
                _animationRows=new();
                void Visit(AnimationHierarchyNode node,int depth)
                {
                    bool group=node.Binding==null,expanded=search!=""||_animationExpanded.Contains(node.Key);
                    _animationRows.Add(new(){Key=node.Key,Label=node.Label,TargetId=node.TargetId,Depth=depth,IsGroup=group,Expanded=expanded,Channels=node.Channels});
                    if(group&&expanded)foreach(var child in node.Children)Visit(child,depth+1);
                }
                foreach(var root in roots)Visit(root,0);
            }
            _animationDope?.RefreshRows();_animationHierarchy?.RefreshRows();
            if(_animationGraph!=null)
            {
                var selected=ActiveAnimationChannels;
                if(selected.Count==0&&_animationWorkspaceMode=="Graph")selected=filtered.Where(AnimationHasKeys).Take(3).ToList();
                _animationGraph.Channels=selected;_animationGraph.Duration=_duration;_animationGraph.Time=_time;_animationGraph.Fps=_fps;_animationGraph.RefreshView();
            }
            RefreshAnimationKeyFields();
        }
        finally{_refreshingAnimationWorkspace=false;}
    }

    void AnimationWorkspaceChanged(){SetTime(_time);RefreshAnimatedProperties();RefreshAnimationWorkspace();RefreshProjectDirtyState();}
    void KeyAnimationChannels(IReadOnlyList<AnimationChannelBinding> channels)
    {
        if(channels.Count==0){Status("Select a property or group to key. Expand the object to choose individual XYZ axes.");return;}
        StopHistoryPlayback();PushUndo();foreach(var binding in channels)PropertyCurveRuntime.Upsert(binding.EnsureCurve(),_time,binding.Read());AnimationWorkspaceChanged();
        Status("Keyed "+channels.Count+" independent parameter(s) at "+_time.ToString("0.###",CultureInfo.InvariantCulture)+" s.");
    }

    internal bool HandleAnimationWorkspaceShortcut(InputEventKey key)
    {
        if(_animationWorkspaceMode=="Clips"||_animationWorkspace==null||!_animationWorkspace.IsVisibleInTree())return false;
        var focus=GetViewport().GuiGetFocusOwner();bool inside=focus!=null&&(_animationWorkspace==focus||_animationWorkspace.IsAncestorOf(focus));
        if(!inside||focus is LineEdit or TextEdit)return false;
        if(key.Keycode==Key.K){KeyAnimationChannels(ActiveAnimationChannels);return true;}
        if(key.Keycode==Key.Delete)
        {
            if(_animationWorkspaceMode=="Graph")
            {
                if(_animationGraph?.SelectedKeyCount>0||focus!=_animationHierarchy)_animationGraph?.DeleteSelectedKeys();else _animationHierarchy?.DeleteSelection();
            }
            else _animationDope?.DeleteSelection();return true;
        }
        if(_animationWorkspaceMode=="Dope sheet"&&key.CtrlPressed)
        {
            if(key.Keycode==Key.C){_animationDope?.CopySelection();return true;}
            if(key.Keycode==Key.V){_animationDope?.PasteSelection();return true;}
            if(key.Keycode==Key.D){_animationDope?.DuplicateSelection();return true;}
        }
        return false;
    }

    sealed partial class AnimationTrackCanvas:Control
    {
        public GoPrevisApp App=null!;public bool HierarchyOnly;
        const float Top=24,RowHeight=24;
        float Left=>HierarchyOnly?Size.X:Math.Clamp(Size.X*.23f,220,320);
        float TrackWidth=>Math.Max(1,Size.X-Left-12);
        double _viewStart,_viewEnd=-1;
        float HeaderOffset=>GetParent() is ScrollContainer scroll?scroll.ScrollVertical:0;
        readonly HashSet<(string Channel,string Key)> _keys=new();
        readonly Dictionary<string,ScalarAnimationCurve> _dragOriginal=new(StringComparer.Ordinal);
        Vector2 _dragFrom,_marqueeTo;bool _dragging,_mutation,_marquee,_scrubbing;
        HashSet<(string Channel,string Key)> _marqueeBase=new();
        PopupMenu? _menu;double _menuTime;
        readonly List<ClipboardKey> _clipboard=PropertyKeyClipboard.Items;
        double End=>_viewEnd<0?App._duration:Math.Max(_viewStart+1.0/App._fps,_viewEnd);
        double Time(float x)=>Math.Clamp(_viewStart+(x-Left)/TrackWidth*(End-_viewStart),0,App._duration);
        double Snap(double time)=>Math.Clamp(Math.Round(time*App._fps)/App._fps,0,App._duration);
        float X(double time)=>Left+TrackWidth*(float)((time-_viewStart)/(End-_viewStart));
        IEnumerable<AnimationChannelBinding> Channels(AnimationWorkspaceRow row)=>App.AnimationRowChannels(row);
        public override void _Ready(){FocusMode=FocusModeEnum.All;MouseFilter=MouseFilterEnum.Pass;ClipContents=true;TooltipText="Expand objects and properties. Click a channel, Ctrl-click to add. Double-click a track to key. Drag keys or select a rectangle; Delete removes animation. Ctrl+C/V copies/pastes keys. Ctrl+wheel zooms time.";}
        public override string _GetTooltip(Vector2 at)
        {
            int index=RowAt(at);if(index<0||index>=App._animationRows.Count)return TooltipText;
            var row=App._animationRows[index];var binding=!row.IsGroup?Channels(row).FirstOrDefault():null;
            return binding==null?row.Label+" · expand to see its animated properties. Double-click the track to capture a group key.":binding.TargetName+" · "+binding.Group+" / "+binding.Label+"\n"+binding.Read().ToString("0.###",CultureInfo.InvariantCulture)+" "+binding.Unit+"\nDiamond: key this property · Star: pin channel · Ctrl-click: add to selection";
        }
        public void RefreshRows(){CustomMinimumSize=new Vector2(HierarchyOnly?215:0,Top+Math.Max(3,App._animationRows.Count)*RowHeight+10);QueueRedraw();}
        public void FitTime(){_viewStart=0;_viewEnd=-1;QueueRedraw();}
        internal Rect2 RowRect(string identity)
        {int i=App._animationRows.FindIndex(row=>row.Channels.Contains(identity)&&!row.IsGroup);return i<0?new():new Rect2(0,Top+i*RowHeight,Size.X,RowHeight);}
        internal Rect2 KeyRect(string identity,string keyId)
        {var row=RowRect(identity);var key=App._animationChannelMap.GetValueOrDefault(identity)?.Curve()?.Keys.FirstOrDefault(k=>k.Id==keyId);return row.Size.Y==0||key==null?new():new Rect2(X(key.Time)-6,row.Position.Y+5,12,12);}
        bool Selected(AnimationWorkspaceRow row)=>row.Channels.Any(App._animationSelectedChannels.Contains);
        static string Fit(string value,float width)=>value.Length*6.2f<=width?value:width<20?"":value[..Math.Min(value.Length,Math.Max(1,(int)(width/6.2f)-1))]+"…";
        static bool At(double a,double b)=>Math.Abs(a-b)<.000001;
        IEnumerable<(AnimationChannelBinding Channel,ScalarKeyData Key)> RowKeys(AnimationWorkspaceRow row)=>Channels(row).SelectMany(c=>PropertyCurveRuntime.OrderedKeys(c.Curve()).Select(k=>(c,k)));
        void Diamond(float x,float y,Color color,float radius=4.5f)=>DrawColoredPolygon(new[]{new Vector2(x,y-radius),new Vector2(x+radius,y),new Vector2(x,y+radius),new Vector2(x-radius,y)},color);
        public override void _Draw()
        {
            if(App==null)return;var font=ThemeDB.FallbackFont;float height=Top+App._animationRows.Count*RowHeight;
            DrawRect(new Rect2(0,0,Size.X,Size.Y),new Color("19191f"));
            if(App._animationRows.Count==0){DrawString(font,new(12,Top+27),"No properties match these filters.",fontSize:11,modulate:App._muted);return;}
            float scrollHeight=GetParent() is ScrollContainer sc?sc.Size.Y:Size.Y;
            int first=Math.Max(0,(int)Math.Floor((HeaderOffset-Top)/RowHeight)),last=Math.Min(App._animationRows.Count,(int)Math.Ceiling((HeaderOffset+scrollHeight)/RowHeight));
            for(int i=first;i<last;i++)
            {
                var row=App._animationRows[i];float y=Top+i*RowHeight;bool selected=Selected(row);
                DrawRect(new(0,y,Left-2,RowHeight-1),selected?new Color("353044"):row.Depth==0?new Color("272530"):new Color("202027"));
                float indent=8+row.Depth*14,label=indent+(row.IsGroup?14:3);var leaf=!row.IsGroup?Channels(row).FirstOrDefault():null;
                if(row.IsGroup)
                {
                    var points=row.Expanded?new[]{new Vector2(indent,y+9),new Vector2(indent+8,y+9),new Vector2(indent+4,y+14)}:new[]{new Vector2(indent+2,y+6),new Vector2(indent+2,y+15),new Vector2(indent+8,y+10.5f)};DrawColoredPolygon(points,App._muted);
                }
                var color=leaf?.Color??(row.Depth==0?App._text:App._muted);float reserve=leaf==null?10:94;
                DrawString(font,new(label,y+16),Fit(row.Label,Left-label-reserve),fontSize:11,modulate:color);
                if(leaf!=null)
                {
                    string value=leaf.Read().ToString("0.###",CultureInfo.InvariantCulture);DrawString(font,new(Left-88,y+16),Fit(value,46),fontSize:10,modulate:App._muted);
                    Diamond(Left-31,y+12,AnimationHasKeys(leaf)?App._accent:App._muted,3.2f);
                    DrawString(font,new(Left-17,y+16),App._animationPinnedChannels.Contains(leaf.Identity)?"★":"☆",fontSize:12,modulate:App._animationPinnedChannels.Contains(leaf.Identity)?new Color("e4c47f"):App._muted);
                }
                if(HierarchyOnly)continue;
                DrawRect(new(Left,y+1,TrackWidth,RowHeight-2),row.IsGroup?new Color("26232e"):new Color("202027"));
                foreach(var group in RowKeys(row).Where(k=>double.IsFinite(k.Key.Time)&&k.Key.Time>=_viewStart&&k.Key.Time<=End).GroupBy(k=>k.Key.Time))
                {
                    bool keySelected=group.Any(k=>_keys.Contains((k.Channel.Identity,k.Key.Id)));
                    Diamond(X(group.Key),y+12,keySelected?new Color("f3d492"):row.IsGroup?new Color("bca4e8"):leaf?.Color??App._accent,keySelected?5:4);
                }
            }
            float header=HeaderOffset;DrawRect(new(0,header,Size.X,Top),new Color("19191f"));DrawString(font,new(8,header+16),"PROPERTIES",fontSize:10,modulate:App._muted);
            if(!HierarchyOnly)
            {
                int ticks=TrackWidth>900?10:6;
                for(int i=0;i<=ticks;i++){float x=Left+TrackWidth*i/ticks;DrawLine(new(x,header+Top),new(x,height),new Color("35333e"));DrawString(font,new(Math.Min(x,Size.X-44),header+16),(_viewStart+(End-_viewStart)*i/ticks).ToString("0.##",CultureInfo.InvariantCulture)+"s",fontSize:10,modulate:App._muted);}
                float head=X(App._time);DrawLine(new(head,header),new(head,height),new Color("e9dcff"),1.5f);DrawColoredPolygon(new[]{new Vector2(head-4,header+Top-7),new Vector2(head+4,header+Top-7),new Vector2(head,header+Top)},new Color("e9dcff"));
            }
            if(_marquee&&_dragFrom.DistanceTo(_marqueeTo)>3){var r=new Rect2(_dragFrom.Min(_marqueeTo),(_marqueeTo-_dragFrom).Abs());DrawRect(r,new Color(.7f,.6f,.94f,.13f));DrawRect(r,App._accent,false);}
        }

        int RowAt(Vector2 at)=>(int)Math.Floor((at.Y-Top)/RowHeight);
        List<(AnimationChannelBinding Channel,ScalarKeyData Key)> HitKeys(AnimationWorkspaceRow row,float x)
        {
            var keys=RowKeys(row).Where(k=>Math.Abs(X(k.Key.Time)-x)<8).OrderBy(k=>Math.Abs(X(k.Key.Time)-x)).ToArray();
            return keys.Length==0?new():keys.Where(k=>At(k.Key.Time,keys[0].Key.Time)).ToList();
        }
        void BeginSelectedDrag(Vector2 at)
        {
            _dragging=true;_mutation=false;_dragFrom=at;_dragOriginal.Clear();
            foreach(string id in _keys.Select(k=>k.Channel).Distinct())if(App._animationChannelMap.TryGetValue(id,out var c)&&c.Curve() is {} curve)_dragOriginal[id]=PropertyCurveRuntime.Copy(curve);
        }
        public override void _GuiInput(InputEvent input)
        {
            if(App._exportBusy||App._aiBusy)return;
            if(input is InputEventKey{Pressed:true,Echo:false} key&&App.HandleAnimationWorkspaceShortcut(key)){AcceptEvent();return;}
            if(input is not InputEventMouseButton b||!b.Pressed)return;
            if(b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                if(!b.CtrlPressed||HierarchyOnly)return;double anchor=Time(b.Position.X),factor=b.ButtonIndex==MouseButton.WheelUp?.8:1.25,span=Math.Clamp((End-_viewStart)*factor,4.0/App._fps,App._duration),ratio=Math.Clamp((b.Position.X-Left)/TrackWidth,0,1);_viewStart=Math.Clamp(anchor-span*ratio,0,Math.Max(0,App._duration-span));_viewEnd=_viewStart+span;QueueRedraw();AcceptEvent();return;
            }
            if(b.ButtonIndex is not(MouseButton.Left or MouseButton.Right))return;
            GrabFocus();int index=RowAt(b.Position);if(b.Position.Y<HeaderOffset+Top){if(!HierarchyOnly&&b.Position.X>=Left){_scrubbing=true;App.StopHistoryPlayback();App.SetTime(Snap(Time(b.Position.X)));AcceptEvent();}return;}
            if(index>=App._animationRows.Count)return;var row=App._animationRows[index];bool add=b.CtrlPressed||b.ShiftPressed;
            if(b.Position.X<Left||HierarchyOnly)
            {
                if(b.ButtonIndex==MouseButton.Right){App.SelectAnimationChannels(row,add);ShowMenu(b.Position);AcceptEvent();return;}
                float indent=8+row.Depth*14;
                if(row.IsGroup&&(b.Position.X<indent+16||b.DoubleClick)){if(!App._animationExpanded.Add(row.Key))App._animationExpanded.Remove(row.Key);App._animationStructureHash=0;App.RefreshAnimationWorkspace();}
                else if(!row.IsGroup&&b.Position.X>=Left-20)
                {
                    var c=Channels(row).First();if(!App._animationPinnedChannels.Add(c.Identity))App._animationPinnedChannels.Remove(c.Identity);App.ExpandAnimationChannel(c);App._animationStructureHash=0;App.RefreshAnimationWorkspace();
                }
                else if(!row.IsGroup&&b.Position.X>=Left-43)App.KeyAnimationChannels(Channels(row).ToList());
                else{_keys.Clear();App.SelectAnimationChannels(row,add);}
                QueueRedraw();AcceptEvent();return;
            }
            var hit=HitKeys(row,b.Position.X);
            if(b.ButtonIndex==MouseButton.Right)
            {
                if(hit.Count>0&&!hit.Any(k=>_keys.Contains((k.Channel.Identity,k.Key.Id)))){_keys.Clear();foreach(var k in hit)_keys.Add((k.Channel.Identity,k.Key.Id));}
                App.SelectAnimationChannels(row,add);ShowMenu(b.Position);AcceptEvent();return;
            }
            if(hit.Count>0)
            {
                bool selected=hit.All(k=>_keys.Contains((k.Channel.Identity,k.Key.Id)));
                if(add&&selected){foreach(var k in hit)_keys.Remove((k.Channel.Identity,k.Key.Id));}
                else{if(!add&&!selected){_keys.Clear();App._animationSelectedChannels.Clear();}foreach(var k in hit){_keys.Add((k.Channel.Identity,k.Key.Id));App._animationSelectedChannels.Add(k.Channel.Identity);}App.RefreshAnimationWorkspace();BeginSelectedDrag(b.Position);}
            }
            else if(b.DoubleClick){App.SetTime(Snap(Time(b.Position.X)));App.KeyAnimationChannels(Channels(row).ToList());}
            else{_marquee=true;_dragFrom=_marqueeTo=b.Position;_marqueeBase=add?new(_keys):new();_keys.Clear();_keys.UnionWith(_marqueeBase);}
            QueueRedraw();AcceptEvent();
        }
        public override void _Input(InputEvent input)
        {
            if(!_dragging&&!_marquee&&!_scrubbing)return;
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){_dragging=_marquee=_scrubbing=false;_dragOriginal.Clear();QueueRedraw();return;}
            if(input is not InputEventMouseMotion motion)return;var at=GetGlobalTransformWithCanvas().AffineInverse()*motion.Position;
            if(_scrubbing){App.SetTime(Snap(Time(at.X)));GetViewport().SetInputAsHandled();return;}
            if(_marquee)
            {
                _marqueeTo=at;_keys.Clear();_keys.UnionWith(_marqueeBase);var rect=new Rect2(_dragFrom.Min(at),(at-_dragFrom).Abs());
                for(int i=0;i<App._animationRows.Count;i++)foreach(var item in RowKeys(App._animationRows[i]))if(rect.HasPoint(new Vector2(X(item.Key.Time),Top+i*RowHeight+12)))_keys.Add((item.Channel.Identity,item.Key.Id));
                foreach(var item in _keys)App._animationSelectedChannels.Add(item.Channel);App.RefreshAnimationWorkspace();QueueRedraw();GetViewport().SetInputAsHandled();return;
            }
            if(!_mutation&&Math.Abs(at.X-_dragFrom.X)<3)return;
            if(!_mutation){App.StopHistoryPlayback();App.PushUndo();_mutation=true;}
            var originals=_dragOriginal.SelectMany(pair=>pair.Value.Keys.Where(k=>_keys.Contains((pair.Key,k.Id)))).ToArray();if(originals.Length==0)return;
            double delta=Math.Round((at.X-_dragFrom.X)/TrackWidth*(End-_viewStart)*App._fps)/App._fps;delta=Math.Clamp(delta,-originals.Min(k=>k.Time),App._duration-originals.Max(k=>k.Time));
            foreach(var pair in _dragOriginal)
            {
                if(!App._animationChannelMap.TryGetValue(pair.Key,out var binding))continue;var curve=binding.EnsureCurve();curve.Keys.Clear();curve.Keys.AddRange(PropertyCurveRuntime.Copy(pair.Value).Keys);
                var moving=curve.Keys.Where(k=>_keys.Contains((pair.Key,k.Id))).ToArray();var destination=moving.Select(k=>k.Time+delta).ToArray();curve.Keys.RemoveAll(k=>!_keys.Contains((pair.Key,k.Id))&&destination.Any(t=>At(t,k.Time)));foreach(var k in moving)k.Time+=delta;curve.Keys.Sort((a,b)=>a.Time.CompareTo(b.Time));
            }
            App.AnimationWorkspaceChanged();QueueRedraw();GetViewport().SetInputAsHandled();
        }

        List<(AnimationChannelBinding Channel,ScalarKeyData Key)> SelectedKeys()=>_keys.Select(item=>
        {
            var binding=App._animationChannelMap.GetValueOrDefault(item.Channel);var key=binding?.Curve()?.Keys.FirstOrDefault(k=>k.Id==item.Key);return(binding,key);
        }).Where(item=>item.binding!=null&&item.key!=null).Select(item=>(item.binding!,item.key!)).ToList();
        public void DeleteSelection()
        {
            var keys=SelectedKeys();var channels=App.ActiveAnimationChannels.Where(AnimationHasKeys).ToList();if(keys.Count==0&&channels.Count==0)return;
            App.StopHistoryPlayback();App.PushUndo();if(keys.Count>0)foreach(var item in keys)PropertyCurveRuntime.Remove(item.Channel.EnsureCurve(),item.Key.Id);else foreach(var channel in channels)channel.Clear();
            _keys.Clear();App.AnimationWorkspaceChanged();QueueRedraw();
        }
        public void ApplySelectedPreset(string preset)
        {var keys=SelectedKeys();if(keys.Count==0)return;App.StopHistoryPlayback();App.PushUndo();foreach(var item in keys)PropertyCurveRuntime.ApplyPreset(item.Channel.EnsureCurve(),item.Key.Id,preset);App.AnimationWorkspaceChanged();}
        static ScalarKeyData CloneKey(ScalarKeyData k)=>new(){Id=k.Id,Time=k.Time,Value=k.Value,Interpolation=k.Interpolation,InSlope=k.InSlope,OutSlope=k.OutSlope,InWeight=k.InWeight,OutWeight=k.OutWeight,BrokenTangents=k.BrokenTangents};
        public void CopySelection()
        {
            var keys=SelectedKeys();if(keys.Count==0)return;_clipboard.Clear();double first=keys.Min(k=>k.Key.Time);
            foreach(var item in keys)_clipboard.Add(new(item.Channel.Identity,item.Channel.Path,item.Key.Time-first,CloneKey(item.Key)));App.Status("Copied "+keys.Count+" animation key(s).");
        }
        public void PasteSelection()
        {
            if(_clipboard.Count==0)return;var active=App.ActiveAnimationChannels;var operations=new List<(AnimationChannelBinding Channel,ClipboardKey Source,double Time)>();bool single=_clipboard.Select(k=>k.Channel).Distinct().Count()==1&&active.Count==1;
            foreach(var source in _clipboard)
            {
                var destination=single?active[0]:active.FirstOrDefault(c=>c.Path==source.Path)??App._animationChannelMap.GetValueOrDefault(source.Channel);double time=Snap(App._time+source.Offset);if(destination!=null&&App._time+source.Offset<=App._duration)operations.Add((destination,source,time));
            }
            if(operations.Count==0)return;App.StopHistoryPlayback();App.PushUndo();_keys.Clear();
            foreach(var operation in operations)
            {
                var src=operation.Source.Data;var key=PropertyCurveRuntime.Upsert(operation.Channel.EnsureCurve(),operation.Time,src.Value);key.Interpolation=src.Interpolation;key.InSlope=src.InSlope;key.OutSlope=src.OutSlope;key.InWeight=src.InWeight;key.OutWeight=src.OutWeight;key.BrokenTangents=src.BrokenTangents;_keys.Add((operation.Channel.Identity,key.Id));
            }
            App.AnimationWorkspaceChanged();QueueRedraw();
        }
        public void DuplicateSelection(){if(SelectedKeys().Count==0)return;CopySelection();double old=App._time;App._time=Snap(SelectedKeys().Min(k=>k.Key.Time)+1.0/App._fps);PasteSelection();App.SetTime(old);}
        void ShowMenu(Vector2 at)
        {
            _menuTime=HierarchyOnly?App._time:Snap(Time(at.X));
            if(_menu==null){_menu=new PopupMenu();AddChild(_menu);_menu.IdPressed+=id=>{if(id==0){App.SetTime(_menuTime);App.KeyAnimationChannels(App.ActiveAnimationChannels);}else if(id==1)DeleteSelection();else if(id==2)CopySelection();else if(id==3)PasteSelection();else if(id>=10)ApplySelectedPreset(new[]{"Linear","Bezier","Hold","Ease in/out","Stop","Auto","Flat"}[id-10]);};}
            _menu.Clear();_menu.AddItem("Add key at this time",0);_menu.AddItem("Delete selected animation",1);_menu.AddItem("Copy keys",2);_menu.AddItem("Paste keys at playhead",3);_menu.AddSeparator();int n=10;foreach(string preset in new[]{"Linear","Bezier","Hold","Ease in/out","Stop","Auto","Flat"})_menu.AddItem(preset,n++);
            _menu.Position=(Vector2I)(GetViewport().GuiEmbedSubwindows?GetGlobalTransformWithCanvas()*at:GetScreenTransform()*at);_menu.Popup();
        }
    }
}
