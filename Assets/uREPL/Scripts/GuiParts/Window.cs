﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace uREPL
{

public class Window : MonoBehaviour
{
	#region [const]
	private const float COMPLETION_TIMEOUT = 0.5f;
	#endregion

	#region [core]
	static public Window selected;

	private Completion completion_ = new Completion();
	private History history_ = new History();

	enum CompletionState {
		Idle,
		Stop,
		WaitingForCompletion,
		Complementing,
	}
	private CompletionState completionState_ = CompletionState.Idle;
	private string completionPartialCode_ = "";

	private float elapsedTimeFromLastInput_ = 0f;
	#endregion

	#region [key operations]
	private KeyEvent keyEvent_ = new KeyEvent();
	public KeyCode openKey = KeyCode.F1;
	public KeyCode closeKey = KeyCode.F1;
	private bool isWindowOpened_ = false;
	#endregion

	#region [content]
	private CommandInputField inputField_;
	private OutputView output_;
	private CompletionView completionView_;
	#endregion

	void Awake()
	{
		InitObjects();
		InitCommands();
		InitEmacsLikeCommands();

		completion_.AddCompletionFinishedListener(OnCompletionFinished);

		isWindowOpened_ = GetComponent<Canvas>().isActiveAndEnabled;
		if (isWindowOpened_) {
			selected = this;
		}
	}

	void InitObjects()
	{
		// Instances
		var container   = transform.Find("Container");
		inputField_     = container.Find("Input Field").GetComponent<CommandInputField>();
		output_         = container.Find("Output View").GetComponent<OutputView>();
		completionView_ = transform.Find("Completion View").GetComponent<CompletionView>();

		// Settings
		inputField_.parentWindow = this;
	}

	private void InitCommands()
	{
		keyEvent_.Add(KeyCode.UpArrow, Prev);
		keyEvent_.Add(KeyCode.DownArrow, Next);
		keyEvent_.Add(KeyCode.LeftArrow, StopCompletion);
		keyEvent_.Add(KeyCode.RightArrow, StopCompletion);
		keyEvent_.Add(KeyCode.Escape, StopCompletion);
		keyEvent_.Add(KeyCode.Tab, () => {
			if (completionView_.hasItem) {
				DoCompletion();
			} else {
				StartCompletion();
			}
		});
	}

	void InitEmacsLikeCommands()
	{
		keyEvent_.Add(KeyCode.P, KeyEvent.Option.Ctrl, Prev);
		keyEvent_.Add(KeyCode.N, KeyEvent.Option.Ctrl, Next);
		keyEvent_.Add(KeyCode.F, KeyEvent.Option.Ctrl, () => {
			inputField_.MoveCaretPosition(1);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.B, KeyEvent.Option.Ctrl, () => {
			inputField_.MoveCaretPosition(-1);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.A, KeyEvent.Option.Ctrl, () => {
			inputField_.MoveTextStart(false);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.E, KeyEvent.Option.Ctrl, () => {
			inputField_.MoveTextEnd(false);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.H, KeyEvent.Option.Ctrl, () => {
			inputField_.BackspaceOneCharacterFromCaretPosition();
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.D, KeyEvent.Option.Ctrl, () => {
			inputField_.DeleteOneCharacterFromCaretPosition();
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.K, KeyEvent.Option.Ctrl, () => {
			inputField_.DeleteAllCharactersAfterCaretPosition();
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.L, KeyEvent.Option.Ctrl, output_.Clear);
	}

	void Start()
	{
		RegisterListeners();
		history_.Load();
	}

	void OnDestroy()
	{
		completion_.Stop();
		UnregisterListeners();
		history_.Save();
		completion_.RemoveCompletionFinishedListener(OnCompletionFinished);
	}

	void Update()
	{
		UpdateKeyEvents();
		UpdateCompletion();
	}

	private void ToggleWindowByKeys()
	{
		if (openKey == closeKey) {
			if (Input.GetKeyDown(openKey)) {
				if (!isWindowOpened_) OpenWindow();
				else CloseWindow();
			}
		} else {
			if (Input.GetKeyDown(openKey)) OpenWindow();
			if (Input.GetKeyDown(closeKey)) CloseWindow();
		}
	}

	private void UpdateKeyEvents()
	{
		ToggleWindowByKeys();

		if (isWindowOpened_) {
			if (inputField_.isFocused) {
				keyEvent_.Check();
			} else {
				keyEvent_.Clear();
			}

			if (IsEnterPressing()) {
				if (completionView_.hasItem) {
					DoCompletion();
				} else {
					OnSubmit(inputField_.text);
				}
			}
		}
	}

	public void OpenWindow()
	{
		selected = this;
		SetActive(true);
		inputField_.Focus();
	}

	public void CloseWindow()
	{
		if (selected == this) {
			selected = null;
		}
		SetActive(false);
	}

	private void SetActive(bool active)
	{
		GetComponent<Canvas>().enabled = active;
		inputField_.gameObject.SetActive(active);
		output_.gameObject.SetActive(active);
		completionView_.gameObject.SetActive(active);
		isWindowOpened_ = active;
	}

	private void Prev()
	{
		if (completionView_.hasItem) {
			completionView_.Next();
		} else {
			if (history_.IsFirst()) history_.SetInputtingCommand(inputField_.text);
			inputField_.text = history_.Prev();
			inputField_.MoveTextEnd(false);
			completionState_ = CompletionState.Stop;
		}
	}

	private void Next()
	{
		if (completionView_.hasItem) {
			completionView_.Prev();
		} else {
			inputField_.text = history_.Next();
			inputField_.MoveTextEnd(false);
			completionState_ = CompletionState.Stop;
		}
	}

	private void UpdateCompletion()
	{
		if (!isWindowOpened_) return;

		switch (completionState_) {
			case CompletionState.Idle: {
				break;
			}
			case CompletionState.Stop: {
				completionState_ = CompletionState.Idle;
				break;
			}
			case CompletionState.WaitingForCompletion: {
				elapsedTimeFromLastInput_ += Time.deltaTime;
				if (elapsedTimeFromLastInput_ >= completionView_.delay) {
					StartCompletion();
				}
				break;
			}
			case CompletionState.Complementing: {
				elapsedTimeFromLastInput_ += Time.deltaTime;
				if (elapsedTimeFromLastInput_ > completionView_.delay + COMPLETION_TIMEOUT) {
					StopCompletion();
				}
				completion_.Update();
				break;
			}
		}

		// update completion view position.
		completionView_.position = inputField_.GetPositionBeforeCaret(completionPartialCode_.Length);
	}

	private void StartCompletion()
	{
		if (inputField_.IsNullOrEmpty()) return;

		var code = inputField_.GetStringFromHeadToCaretPosition();
		completion_.Start(code);

		completionState_ = CompletionState.Complementing;
	}

	private void StopCompletion()
	{
		completion_.Stop();
		completionView_.Reset();
		completionState_ = CompletionState.Idle;
	}

	private void OnCompletionFinished(Completion.Result result)
	{
		completionPartialCode_ = result.partialCode ?? "";
		completionView_.SetCompletions(result.completions);
		completionState_ = CompletionState.Idle;
	}

	private void DoCompletion()
	{
		// for multiline input
		inputField_.RemoveTabAtCaretPosition();

		var completion = completionView_.selectedCompletion;
		inputField_.InsertToCaretPosition(completion);
		inputField_.MoveCaretPosition(completion.Length);
		inputField_.Focus();

		StopCompletion();
	}

	private void RegisterListeners()
	{
		inputField_.onValueChanged.AddListener(OnValueChanged);
		inputField_.onEndEdit.AddListener(OnSubmit);
	}

	private void UnregisterListeners()
	{
		inputField_.onValueChanged.RemoveListener(OnValueChanged);
		inputField_.onEndEdit.RemoveListener(OnSubmit);
	}

	private bool IsEnterPressing()
	{
		if (!inputField_.multiLine) {
			return KeyUtil.Enter();
		} else {
			return (KeyUtil.Control() || KeyUtil.Shift()) && KeyUtil.Enter();
		}
	}

	private void OnValueChanged(string text)
	{
		if (!inputField_.multiLine) {
			text = text.Replace("\n", "");
			text = text.Replace("\r", "");
			inputField_.text = text;
		}

		if (completionState_ != CompletionState.Stop) {
			completionState_ = CompletionState.WaitingForCompletion;
			elapsedTimeFromLastInput_ = 0f;
		}

		Utility.RunOnEndOfFrame(completionView_.Reset);
	}

	private void OnSubmit(string code)
	{
		code = code.Trim();

		// do nothing if following states:
		// - the input text is empty.
		// - receive the endEdit event without the enter key (e.g. lost focus).
		if (string.IsNullOrEmpty(code) || !IsEnterPressing()) return;

		// stop completion to avoid hang.
		completion_.Stop();

		var result = Evaluator.Evaluate(code);
		var item = output_.AddResultItem();

		if (item) {
			switch (result.type) {
				case CompileResult.Type.Success: {
					inputField_.Clear();
					history_.Add(result.code);
					history_.Reset();
					item.type   = CompileResult.Type.Success;
					item.input  = result.code;
					item.output = result.value.ToString();
					break;
				}
				case CompileResult.Type.Partial: {
					// This block should not be reached because the given code is 
					// added a semicolon to end of it. 
					inputField_.Clear();
					item.type   = CompileResult.Type.Partial;
					item.input  = result.code;
					item.output = "The given code is something wrong: " + code;
					break;
				}
				case CompileResult.Type.Error: {
					item.type   = CompileResult.Type.Error;
					item.input  = result.code;
					item.output = result.error;
					break;
				}
			}
		}
	}

	public void OutputLog(Log.Data data)
	{
		output_.OutputLog(data);
	}

	static public GameObject InstantiateInOutputView(GameObject prefab)
	{
		if (Window.selected == null) return null;
		return Window.selected.output_.AddObject(prefab);
	}

	[Command(name = "clear outputs", description = "Clear output view.")]
	static public void ClearOutputCommand()
	{
		if (Window.selected == null) return;
		Utility.RunOnNextFrame(Window.selected.output_.Clear);
	}

	[Command(name = "clear histories", description = "Clear all input histories.")]
	static public void ClearHistoryCommand()
	{
		if (Window.selected == null) return;
		Utility.RunOnNextFrame(Window.selected.history_.Clear);
	}

	[Command(name = "show histories", description = "show command histoies.")]
	static public void ShowHistory()
	{
		if (Window.selected == null) return;

		string histories = "";
		int num = Window.selected.history_.Count;
		foreach (var command in Window.selected.history_.list.ToArray().Reverse()) {
			histories += string.Format("{0}: {1}\n", num, command);
			--num;
		}
		Log.Output(histories);
	}

	[Command(name = "close", description = "Close console.")]
	static public void CloseCommand()
	{
		Utility.RunOnNextFrame(() => {
			if (Window.selected) Window.selected.CloseWindow();
		});
	}
}

}