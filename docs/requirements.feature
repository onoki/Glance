Feature: Main application layout

  Scenario: Viewing the main view
    Given the application is started
    When I am in the main view
    Then I see a navigation pane at the top with tabs:
      | Dashboard |
      | History   |
      | Search    |
      | People |
      | Status Updates |
    And a Settings gear icon is immediately to the left of the New window icon on the right
    And the Settings icon has an accessible name, tooltip, and selected state
    And the gap between Settings and New window matches the shared 1px gap between navigation buttons
    And I see an editable rich text task list for new tasks in the middle
    And next to the title of the new tasks view I see a button to move all new tasks to "Uncategorized"
    And next to the title of the new tasks view I see a button to expand and restore the new tasks view to full screen
    And I see an editable rich text main task list below


Feature: Task completion

  Scenario: Completing a task
    Given a task exists in any task list
    And the task is not completed
    When I mark the task as completed
    Then the task title is shown with strikethrough styling
    And the task text color is gray
    And the completion timestamp of the task is saved

  Scenario: Undoing task completion
    Given a task exists in the history
    And the task is completed
    When I mark the task as not completed
    Then the strikethrough styling is removed
    And the task text color is black
    And the task is moved back to the dashboard

  Scenario: Completing an empty task removes it
    Given a task exists in any task list
    And the task has no visible text in the title or subcontent
    When I mark the task as completed
    Then the task is removed
    And the task is not shown in the history


Feature: Dashboard task categorization

  Scenario: Viewing new tasks
    Given I am on the dashboard tab
    When I view the new tasks task list
    Then I see only tasks under the category "New"

  Scenario: Viewing categorized tasks
    Given I am on the dashboard tab
    When I view the main task list
    Then I see tasks grouped under named categories
    And tasks under the category "New" are not shown


Feature: Task categories

  Scenario: Available task categories
    Given tasks exist in the system
    Then the following task categories are available:
      | New           |
      | Uncategorized |
      | Week starting on YYYY-MM-DD |
      | No date       |
      | Notes         |
      | Repeatable    |

  Scenario: Weekly categories visibility
    Given tasks are categorized by week
    Then only the next 4 weeks are shown
    And any past week that still has tasks is shown


Feature: Floating task ribbon

  Scenario: Showing the task ribbon on focus
    Given I am on the dashboard tab
    And a task has focus
    When the task is focused
    Then a floating ribbon appears above the task
    And the ribbon provides an option to categorize the task
    And the ribbon provides an option to set or unset task recurrence

  Scenario: Configuring task recurrence
    Given a task has focus
    When I configure the task recurrence
    Then I can set the recurrence to:
      | repeatable |
      | daily      |
      | weekly     |
      | monthly    |
    And for weekly recurrence I can select weekdays
    And for monthly recurrence I can select days of the month


Feature: Repeatable tasks

  Scenario: Creating repeatable tasks
    Given a task is marked as repeatable
    Then the task is categorized under "Repeatable"
    And the recurrence configuration is saved

  Scenario: Generating recurring tasks
    Given repeatable tasks exist
    When the day changes or the application is started
    Then new tasks are created based on recurrence rules
    And weekly recurring tasks are generated for the current week only, starting on Monday


Feature: Daily task cleanup

  Scenario: Hiding completed tasks from the dashboard
    Given a task was completed on a day earlier than today
    When the day changes or the application is started
    Then the task is hidden from the main task list on the dashboard


Feature: History view

  Scenario: Viewing task completion history
    Given I navigate to the history tab
    Then I see a list of completed tasks grouped by completion date
    And a collapsed activity chart is available when it contains completions from the last 180 days
    And expanding Activity over time shows daily completion counts

  Scenario: Moving completed tasks to history
    Given I am on the history tab
    And completed tasks exist for today
    When I click the Move completed to history button
    Then the completed tasks are removed from the dashboard
    And the tasks appear in the history list

  Scenario: Restoring a completed task from history
    Given I am on the history tab
    And a completed task exists
    When I mark the task as not completed
    Then the task is removed from the history
    And the task appears in the dashboard


Feature: Search

  Scenario: Viewing the search tab
    Given I navigate to the search tab
    Then I see a search bar
    And I see a search button
    And I see an empty results area

  Scenario: Searching before any search is executed
    Given I am on the search tab
    And no search has been executed
    Then the results area is empty

  Scenario: Searching with no results
    Given I am on the search tab
    When I search for a term that does not exist
    Then I see a message indicating no results

  Scenario: Searching with results
    Given I am on the search tab
    When I search for a term that exists in any task
    Then I see matching tasks from any view regardless of status
    And the tasks are shown as read-only
    And the matching text is highlighted in the search results

  Scenario: Opening a search result in its source view
    Given a search result belongs to Dashboard, People, or History
    When I choose Open source
    Then Glance opens the owning view
    And scrolls to and briefly highlights the matching task
    And I can return to the same search result with Back to search

  Scenario: Opening a note for an archived person
    Given an open search result belongs to an archived person
    When I choose Open source
    Then Glance opens the separate Archived people view
    And highlights the person
    And explains that I must restore the person before opening the note


Feature: Multiple-window consistency

  Scenario: Opening another notes view
    Given Glance is running in the desktop app
    When I choose New window
    Then another native window opens in the same Glance process
    And both windows share the same local database

  Scenario: Reflecting task edits across windows
    Given the application is open in two windows
    And the same task is visible in both instances
    When the task is edited in one instance
    Then the updated content is shown in the other instance

  Scenario: Reflecting task completion across windows
    Given the application is open in two windows
    And a task exists
    When the task is completed in one instance
    Then the task completion state is updated in the other window

  Scenario: Rejecting a stale simultaneous edit
    Given the same task is edited in two windows
    And one window saves first
    When the other window tries to save its older base version
    Then the server returns a conflict without overwriting the first save
    And the second window keeps its local text visible for an explicit decision

  Scenario: Closing immediately after typing
    Given I have just edited a note
    When I close its Glance window immediately
    Then Glance waits for all pending note saves
    And if a save fails the window remains open with the unsaved text


Feature: Subcontent list editing

  Scenario: Subcontent lines and soft line breaks
    Given a task has subcontent
    When I press Shift+Enter inside a subcontent line
    Then a line break is inserted within the same subcontent item

  Scenario: Creating a new subcontent item with Enter
    Given a task has subcontent
    When I press Enter inside a subcontent line
    Then a new subcontent list item is created below
    And if the cursor was in the middle of the line the trailing text moves to the new item
    And pressing Backspace at the start of the new item merges it into the previous line

  Scenario: Merging subcontent items with Backspace
    Given a task has multiple subcontent list items
    When I press Backspace at the start of a subcontent item
    Then the current item merges with the item above

  Scenario: Indenting and outdenting subcontent items
    Given a task has subcontent list items
    When I press Tab inside a subcontent item
    Then the item is indented one level
    When I press Shift+Tab inside a subcontent item
    Then the item is outdented one level

Feature: Task restructuring via Tab

  Scenario: Tab in title converts to subcontent of previous task
    Given multiple tasks exist in a list
    And a task title has focus
    When I press Tab in the task title
    Then the task becomes subcontent of the previous task
    And any existing subcontent is moved under the previous task
    And hidden empty subcontent lines are removed before the moved task

  Scenario: Tab on an empty new task creates editable subcontent
    Given a task has no subcontent
    And I create a new empty task below it
    When I press Tab in the new task title
    Then the new task becomes an empty subcontent line under the previous task
    And the empty subcontent line is ready for typing

  Scenario: Shift+Tab at outermost level splits subcontent into a new task
    Given a task has subcontent items at the outermost level
    And the cursor is within a subcontent item
    When I press Shift+Tab
    Then a new task is created below the current task
    And the sibling-level subcontent is moved to the new task
    And the caret or selection stays at the same relative position in the promoted title
    And this behavior is identical in Dashboard and People, including after a server refresh


Feature: Arrow key navigation

  Scenario: Down arrow from title enters subcontent
    Given a task title has focus
    When I press the down arrow key
    Then the focus moves to the beginning of the first subcontent line

  Scenario: Up arrow from title jumps to previous task
    Given a task title has focus
    And there is a task above
    When I press the up arrow key
    Then the focus moves to the end of the last subcontent line of the task above

  Scenario: Down arrow from subcontent to next title or new task
    Given a task has subcontent
    And the cursor is at the end of the subcontent
    When I press the down arrow key
    Then the focus moves to the beginning of the next task title if one exists
    And if no task exists below a new task is created with an empty title

  Scenario: Up arrow from subcontent to title
    Given a task has subcontent
    And the cursor is at the beginning of the subcontent
    When I press the up arrow key
    Then the focus moves to the task title


Feature: Version visibility and update metadata

  Scenario: Viewing the app version
    Given the application is started
    When I view the settings tab
    Then I see the application version in the About section
    And I see the application version at the top right of the window
    And navigation tabs are aligned to the top left
    And an accessible New window icon is beside the version on the right

  Scenario: Understanding the settings page
    When I open Settings
    Then related options are grouped into Data safety, Maintenance, and About
    And backup, restore, and export have separate labeled subsections
    And search and repeatable-task maintenance are grouped together
    And settings use regular shared typography with 4px gaps within subsections and 8px between groups
    And settings wrap within narrow windows and scroll vertically when needed
    And each group's contents, including subheadings, are indented 12px under its heading

  Scenario: App version persisted on startup
    Given the application is started
    When the server initializes
    Then the current binary version is logged
    And the stored app_version value is logged
    And the schema version is logged
    And the stored app_version is updated after startup


Feature: Update safety invariants

  Scenario: Updating the app without data loss
    Given the application is closed
    When I replace the app binaries
    Then the data folder remains untouched
    And the application starts with the existing data

  Scenario: Updating a portable installation by copying application files
    Given all Glance windows are closed
    When I copy a newly published package over the existing installation
    And preserve the existing data, blobs, backups, recovery, and exports folders
    Then Glance opens the existing notes on next launch
    And applies pending database migrations after its verified pre-migration backup
    And Settings has no in-app update installer

Feature: Backups and maintenance

  Scenario: Backing up data from settings
    Given I am on the settings tab
    When I click the Backup button
    Then a backup is created
    And the last backup time is shown

  Scenario: Reindexing search from settings
    Given I am on the settings tab
    When I click the Reindex button
    Then the search index is rebuilt
    And the last reindex time is shown

  Scenario: Warning banner visibility
    Given the application is started
    When maintenance warnings exist
    Then I see a warning banner
    And I can dismiss a warning


Feature: Attachments

  Scenario: Pasting an image into rich text
    Given a task title or subcontent has focus
    When I paste an image
    Then the image is uploaded
    And the image is inserted into the editor

  Scenario: Resizing an attachment
    Given an image is inserted into a task
    When I select the image
    Then a resize handle appears
    And the resized width is preserved

  Scenario: Removing an attachment
    Given an image is selected in the editor
    When I press Backspace
    Then the image is removed


Feature: Drag and drop ordering

  Scenario: Reordering tasks within a category
    Given a category has multiple tasks
    When I drag a task within the category
    Then the task order is updated

  Scenario: Moving tasks across categories
    Given tasks exist in multiple categories
    When I drag a task to a different category
    Then the task is moved to that category


Feature: Recurrence controls

  Scenario: Allowed recurrence types
    Given a task has recurrence controls visible
    Then I can set the recurrence to:
      | weekly  |
      | monthly |

  Scenario: Weekly recurrence selection
    Given a task is set to weekly recurrence
    When I select weekdays
    Then the selected weekdays are saved

  Scenario: Monthly recurrence selection
    Given a task is set to monthly recurrence
    When I enter month days
    Then the selected days are saved

  Scenario: Remembering recurrence configuration
    Given a task has a weekly or monthly recurrence configured
    When I move it out of Repeatable
    Then the recurrence configuration is retained for later use


Feature: Category interaction

  Scenario: Category actions open deliberately
    Given a task is visible on the dashboard
    When I focus the task and activate its Move to category button
    Then category options are shown

  Scenario: This week scheduling on category change
    Given a task is moved into the This week category
    Then its scheduled date is set to today

  Scenario: Dragging within This week changes the scheduled day
    Given a task is in the This week category
    When I drag the task to a different weekday group
    Then its scheduled date matches the target weekday


Feature: Dashboard layout and scrolling

  Scenario: Horizontal columns on wide screens
    Given I am on the dashboard tab
    Then tasks are shown in horizontal columns by category
    And each column scrolls vertically
    And the dashboard scrolls horizontally across columns

  Scenario: This week weekday grouping
    Given tasks exist in the This week category
    Then tasks are grouped by weekday
    And weekday headers are sticky within the column


Feature: Title-only tasks and deletion

  Scenario: Creating title-only tasks
    Given a task has no subcontent
    When I press Enter at the end of the title
    Then a new task is created below
    And focus moves to the new task title

  Scenario: Splitting a task title into a new task
    Given a task title has focus
    And the caret is in the middle of the title
    When I press Enter
    Then the text after the caret becomes the title of a new task below
    And the text after the caret is removed from the original task
    And the original task subcontent is moved to the new task
    And pressing Backspace at the start of the new title merges the tasks back together

  Scenario: Removing the last subcontent item
    Given a task has a single empty subcontent item
    When I press Backspace in the subcontent
    Then the subcontent item is removed
    And focus moves to the task title

  Scenario: Deleting an empty task
    Given a task has no title and no subcontent
    When I press Backspace
    Then the task is deleted
    And focus moves to the previous task if it exists


Feature: Keyboard shortcuts

  Scenario: Insert the current date at the caret
    Given I am editing a task title or subcontent in Dashboard or People
    When I press Shift+Alt+D
    Then today's local calendar date is inserted as YYYY-MM-DD
    And any selected text is replaced by the date
    And the caret is immediately after the inserted date
    And the insertion is saved and can be undone like ordinary typing
    And the shortcut does not modify read-only tasks

  Scenario: Formatting shortcuts
    Given a rich text editor has focus
    When I press Ctrl+B or Cmd+B
    Then the selected text is bolded
    When I press Ctrl+I or Cmd+I
    Then the selected text is italicized
    When I press Ctrl+K or Cmd+K
    Then I can create, edit, or remove an http, mail, mapped-drive, UNC, or file hyperlink

  Scenario: Recognizing and opening hyperlinks
    Given I type or paste a web URL in a rich text editor
    Then it is stored as a hyperlink automatically
    And clicking a link in a read-only view opens it with the registered application
    And Ctrl+clicking a link while editing opens it without preventing normal caret placement
    And executable, script, shortcut, data, and javascript targets are rejected

  Scenario: Highlight shortcuts
    Given a rich text editor has focus
    When I press Ctrl+4 or Cmd+4
    Then green highlight is toggled for the whole line
    When I press Ctrl+5 or Cmd+5
    Then yellow highlight is toggled for the whole line
    When I press Ctrl+6 or Cmd+6
    Then red highlight is toggled for the whole line
    And if multiple subcontent lines are selected the highlight applies to each line

  Scenario: Undo and redo shortcuts
    Given a rich text editor has focus
    When I press Ctrl+Z or Cmd+Z
    Then the last edit is undone even if it was in another task or subcontent
    When I press Ctrl+R, Ctrl+Y, Cmd+R, or Cmd+Y
    Then the last edit is redone

Feature: Window size

  Scenario: Remembering window size
    Given I resize the application window
    When I restart the application
    Then the window size is restored
    And the minimum size is 100x100 pixels
    And if the window was maximized on close the last non-maximized size is restored
    And the last normal window position is restored on the saved monitor
    And a minimized window also restores its last normal bounds
    And the restored window fits entirely inside that monitor's current work area
    And if the saved monitor is absent the window is centered on the primary monitor
    And normal bounds from the last successfully closed window are used for the next launch
    And another window opened during the session also fits its current monitor
    And normal X and Y coordinates survive closing on either horizontally adjacent monitor
    And native window creation does not replace the restored coordinates with default placement

Feature: UI cache

  Scenario: Loading fresh UI assets after updates
    Given the application binaries have been updated
    When the application starts
    Then the UI is loaded without stale cached assets

  Scenario: Subcontent checkbox shortcut
    Given a subcontent list item has focus
    When I press Ctrl+1 or Cmd+1
    Then an empty checkbox marker is inserted at the start of the line
    When I press Ctrl+1 or Cmd+1 again
    Then the checkbox marker is toggled to checked
    When I press Ctrl+1 or Cmd+1 a third time
    Then the checkbox marker is removed

  Scenario: Title completion shortcut
    Given a task title has focus
    When I press Ctrl+1 or Cmd+1
    Then the task is marked as completed
    When I press Ctrl+1 or Cmd+1 again
    Then the task is marked as not completed

  Scenario: Title completion shortcut is disabled for read-only tasks
    Given a read-only task title has focus
    When I press Ctrl+1 or Cmd+1
    Then the task completion state does not change

  Scenario: Subcontent star shortcut
    Given a subcontent list item has focus
    When I press Ctrl+2 or Cmd+2
    Then a yellow star marker (⭐) is inserted at the start of the line
    When I press Ctrl+2 or Cmd+2 again
    Then the star marker is removed
    And if a checkbox marker is present it appears before the star marker

  Scenario: Search shortcut
    Given I am not focused on an editor
    When I press Ctrl+F or Cmd+F
    Then the Search tab is activated


Feature: Search matching

  Scenario: Partial word matches
    Given tasks exist with words in titles or subcontent
    When I search for a partial word
    Then matches include occurrences within the word


Feature: Project status updates

  Scenario: Marking status input
    Given a Dashboard or History task line has focus
    When I press Ctrl+7 or Cmd+7
    Then that title or subcontent line shows a durable status input marker
    And the whole task is included when status input is collected

  Scenario: Recollecting status input on the same day
    Given a status input package already exists for today
    When I collect status input again
    Then the daily package is replaced
    And its input revision is incremented

  Scenario: Importing completed status output
    Given I upload a completed StatusSummary.json
    Then Glance validates its schema, report identity, revision, and unchanged input
    And Excel is available as one worksheet
    And PowerPoint is available as one slide


Feature: Person-specific notes

  Scenario: Managing people
    Given I am on the People tab
    Then I can add, rename, archive, and restore people
    And I can create and assign user-defined tags
    And I can drag people tabs to reorder them

  Scenario: Browsing many people
    Given names do not fit on one navigation row
    Then person buttons wrap onto additional rows
    And each assigned tag appears as a small stable-colored dot beside a person's name
    And hovering a dot reveals the tag name
    And a shared legend explains the dot colors without repeating tag labels for every person
    And no tag filters are required
    And Tags, Rename, and Archive use the same button styling
    And legend dots and their labels are vertically centered together
    And the add-person button uses the common app font at regular weight

  Scenario: Completing a person note
    Given a note exists in a person's list
    When I mark the note complete
    Then it remains struck through and can be unchecked until the next local day
    And after that it remains available in History

  Scenario: Editing a person note list
    Given I selected a person with no active notes
    Then an empty editable note is ready without pressing an add button
    And Enter, arrow navigation, merging, splitting, and drag ordering match Dashboard task behavior
    And newly mounted title and subcontent editors receive requested keyboard focus
    And text edits in different People notes support session Undo and Redo

  Scenario: Copying tasks between Dashboard and People
    Given a task exists in Dashboard or a person's list
    When I send it to a person, tag group, or Dashboard
    Then an independent copy is created
    And the source remains open with a dismissible sent marker

  Scenario: Completing a person's note
    Given a note exists in a person's list
    When I complete it
    Then it appears in History with the person's name
    And it counts in History activity


Feature: Compact task lists and long links

  Scenario: Resizing and reading categories
    Given I am on Dashboard
    Then every category including New tasks can be resized with the mouse
    And category widths are remembered
    And expanding and restoring New tasks preserves its custom width
    And task text stays aligned at the left of each category
    And long text and URLs wrap even without spaces
    And horizontal dashboard navigation across categories remains available

  Scenario: Contextual overlays without layout jumps
    Given a task is visible in Dashboard or People
    When I focus an editor or action control within the task
    Then its actions and date appear together on the right above the task
    And opaque backgrounds appear only behind individual buttons and labels
    And the unused overlay area is transparent and allows clicking the task underneath
    And the overlay may cover earlier lines without reserving row height
    And task and line positions do not change when the overlay appears or disappears
    And actions stay within the visible horizontal portion of the category and window
    And the delete action remains the rightmost action

  Scenario: Compact URL presentation preserves data
    Given an automatically linked URL is longer than 60 characters
    When its editor is not focused
    Then it may display the protocol and domain followed by an ellipsis
    And focusing the editor reveals the full text for editing
    And the full URL remains intact for copying, saving, exporting, and opening
    And a custom link label is not replaced
    And no external shortening service is used

  Scenario: Typing beside a hyperlink
    Given the caret is immediately before or after a hyperlink
    When I type ordinary text or spaces
    Then the new text is not part of the link
    And the original hyperlink destination is unchanged

  Scenario: Question marker shortcut
    Given an editable task title or subcontent line has focus
    When I press Ctrl+3 or Cmd+3
    Then a colored question mark is toggled at the start of the line after checkbox and star markers
    And the marker survives saving and reopening

Feature: Data portability

  Scenario: Exporting all durable notes
    Given Glance contains Dashboard, People, History, and status-update data
    When I export all notes from Settings
    Then I receive a versioned ZIP with lossless JSON and readable offline HTML
    And note images and status JSON are included
    And a manifest provides a SHA-256 hash and byte length for every payload file
    And generated Excel and PowerPoint status files are not included

  Scenario: Preventing incomplete future exports
    Given a durable database table or column is added
    And it has not been classified in the portable export contract
    When an export is requested
    Then Glance refuses the export with an actionable error


Feature: Verified backup and recovery

  Scenario: Creating an automatic restore point
    Given notes changed since the last verified backup
    And at least one hour has elapsed
    When scheduled maintenance runs
    Then Glance creates and verifies a compressed local snapshot
    And does not create another snapshot for an unchanged hour

  Scenario: Suggesting restore alternatives
    Given verified snapshots exist at several actual times
    When I open Data safety settings
    Then those snapshots are offered with time, counts, reason, and verification state
    And I do not need to guess a timestamp

  Scenario: Restoring a selected snapshot
    Given I choose an available verified snapshot
    When I confirm the whole-state restore
    Then every open window flushes its pending note saves
    And Glance makes an emergency verified backup
    And restarts before replacing the database, attachments, and status JSON
    And the pre-restore state remains in a rescue folder

  Scenario: Startup verification fails
    Given the existing SQLite database fails structural or foreign-key verification
    When Glance starts
    Then migrations do not run
    And the live database is not vacuumed, rebuilt, moved, or replaced
    And Glance enters recovery mode so I can select a verified restore point

  Scenario: A second backup location is unavailable
    Given a second backup location is configured but offline
    When a backup is due
    Then the verified local backup still succeeds
    And Glance shows a warning that the second copy failed

Feature: Pixel typography and shared controls

  Scenario: Task title alignment with completion checkbox
    Then the completion checkbox and the first title line have the same vertical center
    And a wrapped title remains aligned by its first line
    And task titles have no extra bullet and sit beside the checkbox with a 2px gap
    And subcontent keeps its list bullets and optional checklist markers

  Scenario: BigBlue Terminal at 150 percent display scaling
    Then app text uses BigBlue TerminalPlus at 8 CSS pixels
    And controls use a shared neutral, emphasized, or danger style with 16px minimum height
    And UI text uses regular weight without synthetic bold or italic
    And only BigBlue TerminalPlus is bundled and listed in Font licenses
    And ordinary unpressed buttons, task titles, and subcontent use the same dark neutral text color
    And link, highlight, completion, selection, and danger colors retain their meaning
    And task rich text retains user-applied synthetic bold and italic

  Scenario: Task actions follow focus
    Given a task editor has focus
    When I hover another task
    Then only the focused task's action bar remains visible
    And its controls remain accessible when I move focus into them

  Scenario: Returning to Glance cannot activate dismissed task actions
    Given a task action bar or its menu is open
    When I switch to another window and click back into Glance
    Then previously hidden action bars and menus cannot receive that click
    And restored editor focus alone does not reopen them
    And clicking task text immediately shows that task's action bar after the click is delivered
    And previously open menus and other tasks' action bars remain dismissed
    And an editing keystroke can also show the focused task's actions

  Scenario: Reordering with the whole-task selector
    When I drag a task's narrow selection handle above or below another task
    Then the entire task moves to the indicated position
    And clicking the handle still selects the task for clipboard operations
    And dragging editor text does not reorder the task

  Scenario: Choosing tag colors
    When I choose a tag color in the People Tags menu
    Then its dots and legend use that color across people and windows
    And the color survives restart, rename, backup and portable export
    And the active color is saved without a confirmation button when I click away from the picker
    And rapid color changes retain the last chosen color even when saves are slow
    And the color picker, Rename, and red delete cross share the same tag row

  Scenario: Remembering the selected person
    Given I selected an active person
    When I leave People and return in the same window
    Then that person is selected again
    And an unavailable or archived selection falls back to the first active person

  Scenario: Rapid Enter then Tab
    Given the caret is at the end of a task title
    When I immediately press Enter then Tab before new-task creation finishes
    Then a new empty subtask is focused beneath that task
    And the original title and all existing subcontent remain intact

  Scenario: Deleting a dirty empty task
    When I erase a task and press Backspace again
    Then deletion retires that task's pending save registration
    And no later save of the deleted task blocks closing the app
    And Undo remains available

  Scenario: Inspecting link destinations
    When I hover a link in task text
    Then its tooltip reveals the full destination even if its label is shortened or aliased

Feature: Whole-task selection and clipboard

  Scenario: Narrow task selection
    Given I am in Dashboard or People
    When I click the 8px task-selection handle beside the checkbox
    Then the whole title and all nested subcontent are selected
    And the handle and gap add only 10px of row width
    And selection does not change row height or task completion
    And Ctrl-click toggles tasks and Shift-click selects a visual range
    And Ctrl+Shift+Space selects the current task from its editor
    And Escape returns to ordinary text editing

  Scenario: Safe grouped cut
    Given whole tasks are selected
    When I press Ctrl+X
    Then the full task payload is written to the system clipboard before deletion
    And failed clipboard access or failed saving leaves the tasks intact
    And stale revisions cannot delete newer edits from another window
    And one Undo restores the cut group with its original IDs
    And interrupted requests retain recoverable undo progress

  Scenario: Pasting complete tasks
    Given the clipboard contains whole Glance tasks
    When I paste below a task in Dashboard or People
    Then new independent tasks preserve titles, rich formatting, links, and nested content
    And they use the destination person or category and date with recurrence disabled
    And completion and send history are not copied
    And repeated paste creates new IDs
    And one Undo removes the pasted group without affecting the originals
    And newer edits from another window are protected during Undo and Redo
    And attachment references work across windows of the same database
    And attachment payloads from another database are rejected before creation
    And ordinary text copy and paste remain unchanged

  Scenario: Compact text rendering and highlights
    Given Windows display scaling is 150 percent
    Then BigBlue TerminalPlus remains at 8 CSS pixels
    And text origins are not repeatedly repositioned by a self-correcting layout loop
    And existing control sizes stay unchanged
    And ordinary text stays dark neutral while semantic colors remain meaningful
    And green and red highlights have pale backgrounds, including previously saved marks

  Scenario: Bottom insertion space and caret visibility
    Given a Dashboard category or People task list
    Then at least 64 CSS pixels of blank insertion space follows its tasks
    When I click that space
    Then an editable task is appended to that list
    When I type near the visible edge
    Then the column scrolls enough to keep the caret visible with room below it
    And editing an earlier visible task does not scroll to the end

  Scenario: Empty task Delete key
    Given a task has no title or subcontent
    When I press Delete or Backspace in its editor
    Then the empty task is removed using the same safe deletion behavior
    And Delete never merges backward into a preceding task

  Scenario: Backspace join position
    Given adjacent task titles First task and Second task and the first task has no subcontent
    When I press Backspace immediately before the S of Second task
    Then their titles and subcontent are combined
    And the caret stays immediately before the S in the combined title
    And this works in Dashboard and People immediately after saving an edit

  Scenario: Compact focus and feedback
    Then the focused task has a subtle ownership indicator without moving surrounding rows
    And category menus open by click or keyboard activation, never hovering
    And Escape, outside clicks, and window blur close task menus
    And actions distinguish Move from Send a copy
    And a move offers brief Undo feedback only while it remains the next undo entry
    And save feedback is shown only for pending changes, saving, or failures
    And whole-task selection shows count and copy, cut, and escape hints

  Scenario: People tag scope
    Given a person is selected
    Then their name appears beside their controls
    And personal tag assignment is separate from collapsed Manage shared tags controls
    And shared tag controls explain that changes affect everyone using the tag

  Scenario: Compact secondary views
    Then Search results show source context with Open source beside copy controls
    And Up and Down navigate Open source buttons and Enter opens the source
    And History omits an empty chart and collapses populated charts behind Activity over time
    And Settings keeps group indentation and collapses restore controls unless recovery requires them
    And Status Updates explains the next step with technical details in a disclosure

  Scenario: Grayscale desktop text rendering
    Given Glance runs in its Windows desktop host
    Then its WebView2 processes request grayscale text antialiasing with disable-lcd-text
    And the font size remains 8 CSS pixels
    And Windows-wide font settings are unchanged
    And this is not described as disabling all font smoothing

  Scenario: Neutral Add Person control
    Then + Person uses the same neutral button treatment as Rename and Archive
    And dark selected styling identifies the selected person or navigation tab

  Scenario: Stable person switching
    When I choose another person
    Then the current person's name and tasks stay together until the new list is loaded
    And the selected person, heading, and tasks change together without cross-person list animations
    And stale responses and background polling cannot replace a newer requested person

  Scenario: Connected task ownership outline
    Given a task's action bar is visible
    Then its top blue line extends across the bar to meet the left task line
    And neither line reserves layout space or blocks clicks through empty bar space
    When I drag a task
    Then both ownership lines and the action bar are hidden together
    And window blur hides the ownership indication with the controls

  Scenario: Delete moves forward and Backspace moves backward
    Given an entirely empty task has a following task
    When I press Delete in its editor
    Then it is removed and the caret goes to the beginning of the following task title
    When I instead press Backspace in an entirely empty task
    Then the previous task receives focus as before
    And when Delete has no following task it falls back to the previous task
    And these rules apply in Dashboard and People

  Scenario: Deletion transfers the complete focus indicator
    When Delete removes an empty task and focuses the following title
    Then both the left outline and full-width top outline belong to that task
    And the action bar is positioned again after the list layout changes
    And explicit editing navigation resumes dismissed destination controls without reopening menus
    And native window activation alone still cannot reopen dismissed controls

  Scenario: Backspace merges only within the current Dashboard column
    Given Dashboard tasks from different categories are interleaved in storage order
    When I press Backspace at the beginning of a task title
    Then it merges only into the preceding visible task in that same column
    And its caret remains at the join
    And Backspace on the first task in a column does not merge into another column


  Scenario: Delete joins the next title when the current task has no subcontent
    Given the caret is at the end of a title with no subcontent and no text selection
    When I press Delete
    Then the following task title is appended to this title without inserting a space
    And its subcontent becomes this task's subcontent
    And the caret stays at the join

  Scenario: Delete joins the first subcontent line into the title
    Given the caret is at the end of a title with subcontent
    When I press Delete
    Then only the first logical subcontent line is appended to the title
    And its remaining hard-break text, paragraphs, children, and later rows are preserved as subcontent
    And children of a consumed parent bullet are promoted one level
    And the caret stays at the join

  Scenario: Delete at the end of subcontent joins the next task
    Given the caret is at the end of the last logical subcontent line
    When I press Delete
    Then the following task's title is appended to that line
    And its subcontent is appended as subsequent subcontent rows
    And the current task's title is unchanged
    And the caret stays immediately before the appended title

  Scenario: Backspace respects the preceding task's subcontent
    Given the caret is at the beginning of a task title with no text selection
    When I press Backspace
    Then its title is appended to the preceding task's final subcontent line if one exists
    And otherwise its title is appended to the preceding task title
    And its remaining subcontent is appended as subsequent rows
    And the caret stays at the join

  Scenario: Safe logical-line joins
    Then Dashboard and People use the same line-joining rules
    And nested structure, rich marks, links, inline images, and remaining blank lines are preserved
    And a soft-wrapped visual line is not a separate logical line
    And a join never crosses a Dashboard column or a person boundary
    And no adjacent task means there is nothing to join
    And Delete on a text selection retains native selection deletion
    And repeated keys cannot overlap an in-flight join
    And Undo restores both original tasks and Redo joins them again
    And entirely empty task deletion retains its forward Delete and backward Backspace focus rules

  Scenario: Practical zoom with monitor-local preferences
    Given BigBlue TerminalPlus remains at an 8 CSS pixel base size
    Then compact minus, percentage reset, and plus controls appear in the top bar between the version number and Settings
    And Ctrl+wheel and Ctrl+plus/minus use the same practical intermediate zoom steps
    And clicking the percentage or pressing Ctrl+0 resets extra Glance zoom to 100 percent
    And Windows display scaling remains in effect
    And near-whole multiples of the native 12-device-pixel font height show an asterisk
    And the percentage tooltip explains that the asterisk suggests sharpness without guaranteeing it
    And no text-origin correction loop runs after zoom, layout, or monitor changes
    And desktop zoom is remembered per monitor on this PC, outside portable app data
    When the window changes monitors
    Then it adopts that monitor's saved zoom, defaulting to 100 percent for an unseen monitor

  Scenario: Horizontal task navigation in Dashboard and People
    Given no selection or modifier key is active
    When I press Left at the beginning of a task title
    Then the caret moves to the final subcontent position of the previous task, or its title end
    When I press Right at the end of the task's final content, or a title with no subcontent
    Then the caret moves to the next task title at position 1
    And Right at a title with subcontent moves to the beginning of its subcontent
    And Left at the beginning of subcontent moves to the title end
    And navigation stays within the current visible column or person
    And no new task is created at the last boundary
    And modified arrows and selection behavior remain native

  Scenario: Category moves clear obsolete scheduling metadata
    Given any Dashboard category including New tasks, Uncategorized, Notes, No date, This week, Next week, and Repeatable
    When I move a task to any other category using a menu or dragging
    Then its scheduled date and recurrence match the destination, including explicit clearing
    And its title and subcontent remain unchanged
    And an ordinary text update that omits scheduling fields preserves its category

  Scenario: People navigation alignment
    Then the top edges of + Person, person names, and Archived align without extra wrapper borders
    And drag indicators do not change button positions or heights
