Feature: Main application layout

  Scenario: Viewing the main view
    Given the application is started
    When I am in the main view
    Then I see a navigation pane at the top with tabs:
      | Dashboard |
      | People    |
      | Search    |
      | History   |
    And a Settings gear icon is immediately to the left of the New window icon on the right
    And the Settings icon has an accessible name, tooltip, and selected state
    And the gap between Settings and New window matches the shared 1px gap between navigation buttons
    And I see an editable rich text task list for new tasks in the middle
    And next to the title of the new tasks view I see a button to move all new tasks to "Uncategorized"
    And every Dashboard column has a compact maximize and restore control on the right of its title, including New tasks
    And maximizing a column fills the content area while keeping the top navigation available
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

  Scenario: Archiving completed tasks at local midnight
    Given a Dashboard or People task was completed on a day earlier than today
    When local midnight arrives or Glance regains focus after sleep or hibernation
    Then pending edits are saved before the task is archived
    And the task leaves its active list without changing its completion timestamp or content
    And History is refreshed if visible
    And failed saves or archive requests remain eligible for retry
    And application startup also excludes earlier completions from active lists


Feature: History view

  Scenario: Viewing task completion history
    Given I navigate to the history tab
    Then I see a list of completed tasks grouped by completion date
    And an expanded activity chart shows daily completion counts from the last 180 days when there are completions
    And my Activity over time expand or collapse choice is remembered across restarts on this PC
    And daily counts use the recorded completion timestamp in the current local timezone
    And counts include completed Dashboard and People notes even before manual archiving
    And archiving does not change the completion day or count
    And restoring or deleting a completed note removes its activity count

  Scenario: Moving completed tasks to history
    Given I am on the history tab
    And completed tasks exist for today
    When I click the Move completed to history button
    Then today's completed tasks are removed from Dashboard and People lists
    And completion timestamps remain unchanged because archiving is stored separately
    And the tasks remain in the history list under their completion dates
    And repeated archiving is idempotent
    And People notes retain their person attribution and can be restored to that person's list
    And open notes, earlier completions, and deleted notes are unchanged

  Scenario: Restoring a completed task from history
    Given I am on the history tab
    And a completed task exists
    When I mark the task as not completed
    Then the task is removed from the history
    And the task returns to its original Dashboard or People list with its archive marker cleared


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
    And scrolls to and highlights the matching task for five seconds before fading out
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
    And its existing subcontent is nested one level below its former title
    And deeper descendants retain their relative nesting, rich formatting and images
    And existing empty subcontent lines are preserved before and after the moved task

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
    And the promoted item's children become the new task's subcontent one level higher
    And promoting a just-demoted task reverses its hierarchy without losing blank lines or descendants
    And the caret or selection stays at the same relative position in the promoted title
    And this behavior is identical in Dashboard and People, including after a server refresh


Feature: Arrow key navigation

  Scenario: Down arrow from title enters subcontent
    Given a task title has focus
    When I press the down arrow key
    Then the caret moves to the end of the first subcontent paragraph
    And wrapped title lines retain native vertical navigation until the last visual line

  Scenario: Up arrow from title jumps to previous task
    Given a task title has focus
    And there is a task above
    When I press the up arrow key
    Then the focus moves to the last subcontent line of the task above, or its title if it has no subcontent
    And cross-task movement preserves start or end position, or the nearest horizontal position for a middle caret

  Scenario: Down arrow from subcontent to next title
    Given a task has subcontent
    And the cursor is at the end of the subcontent
    When I press the down arrow key
    Then the focus moves to the next task title if one exists
    And cross-task movement preserves start or end position, or the nearest horizontal position for a middle caret
    And if no task exists below in the same column or person the caret stays in place
    And no task is created by any arrow key

  Scenario: Up arrow from subcontent to title
    Given a task has subcontent
    And the cursor is on the first visual line of the first subcontent paragraph
    When I press the up arrow key
    Then the caret moves to the end of the task title


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
    And settings use shared typography with 4px gaps within subsections and 8px between groups
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
    And the image never displays wider than its intrinsic width, even with an old oversized saved width
    And large images fit the available column width with their aspect ratio preserved
    And the handle allows smaller widths but cannot enlarge an image beyond its intrinsic width

  Scenario: Removing an attachment
    Given an image is selected in the editor
    When I press Backspace
    Then the image is removed


Feature: Drag and drop ordering

  Scenario: Reordering tasks within a category
    Given a category has multiple tasks
    When I drag a task within the category
    Then the task order is updated
    And the whole target task is a drop area, using its vertical midpoint for before or after
    And an insertion marker appears only at whole-task boundaries, never inside subcontent
    And crossing paragraphs inside the target does not reset the marker
    And the dragged source has no insertion marker
    And dropping preserves title, subcontent, nesting, and attachments
    And This week calculates ordering among neighbours on the target scheduled day

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

  Scenario: Enter at the beginning of a populated title
    Given a task title has focus with a collapsed caret at its beginning
    When I press Enter
    Then an empty task is inserted immediately above in the same column or person
    And it inherits the scheduled date of the original task
    And the original task keeps its identity, title, complete subcontent, and metadata
    And the caret stays at the beginning of the original title
    And a slow or failed creation never clears or relocates the original task
    And Undo removes only the inserted empty task
    And Redo restores that empty task in the same position and category

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
    And the new task is saved before the original title or subcontent is shortened
    And a failed creation leaves the original task intact
    And typing during a slow creation is never replaced by an older split snapshot
    And an incomplete split reports the error and keeps the available copies
    And Undo and Redo restore the complete split in Dashboard and People

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
    And maximizing and restoring any column preserves custom widths and the normal horizontal viewport
    And a disappearing maximized category restores the regular view
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

Feature: Compact typography and shared controls

  Scenario: Task title alignment with completion checkbox
    Then the completion checkbox and the first title line have the same vertical center
    And a wrapped title remains aligned by its first line
    And task titles have no extra bullet and sit beside the checkbox with a 2px gap
    And subcontent keeps its list bullets and optional checklist markers

  Scenario: IBM Plex Sans with the neutral Notepad palette
    Then Dashboard, People, Search, History and Settings use IBM Plex Sans at 7 CSS pixels
    And plain task titles and subcontent share the same size and 1.35 line height
    And app and header backgrounds are off-white with near-black ordinary text and neutral gray borders
    And selected tabs use a pale gray fill and a dark inset bottom rule without inverted text
    And controls use a shared neutral, emphasized, or danger style with 16px minimum height
    And task text and ordinary or selected controls use regular weight without synthetic bold or italic
    And section headings use genuine semibold at the same 7px size, with 500-weight Settings subsection headings
    And only the IBM Plex Sans family is bundled and listed in Font licenses with its SIL OFL 1.1 attribution
    And genuine upright and italic variable faces support regular and bold task formatting
    And ordinary unpressed buttons, task titles, and subcontent use the same dark neutral text color
    And underlined links, highlights, completion, selection, tags, warnings and danger actions retain their meaning
    And task rich text retains user-applied bold and italic without synthetic faces

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
    Then IBM Plex Sans remains at 7 CSS pixels
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
    And History omits an empty chart and shows populated charts by default with a remembered Activity over time disclosure
    And Settings keeps group indentation and collapses restore controls unless recovery requires them

  Scenario: Grayscale desktop text rendering
    Given Glance runs in its Windows desktop host
    Then its WebView2 processes request grayscale text antialiasing with disable-lcd-text
    And the font size remains 7 CSS pixels
    And Windows-wide font settings are unchanged
    And this is not described as disabling all font smoothing

  Scenario: Neutral Add Person control
    Then + Person uses the same neutral button treatment as Rename and Archive
    And selected fill and an inset rule identify the selected person or navigation tab

  Scenario: Stable person switching
    When I choose another person
    Then the current person's name and tasks stay together until the new list is loaded
    And the selected person, heading, and tasks change together without cross-person list animations
    And stale responses and background polling cannot replace a newer requested person

  Scenario: Connected task ownership outline
    Given a task's action bar is visible
    Then its top focus line extends across the bar to meet the left task line
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
    Given IBM Plex Sans remains at a 7 CSS pixel base size
    Then compact minus, percentage reset, and plus controls appear in the top bar between the version number and Settings
    And Ctrl+wheel and Ctrl+plus/minus use the same practical intermediate zoom steps
    And clicking the percentage or pressing Ctrl+0 resets extra Glance zoom to 100 percent
    And Windows display scaling remains in effect
    And text sizes mapping near whole device pixels show an asterisk
    And the percentage tooltip explains this is only an alignment hint for outline text, without guaranteeing sharpness or removing antialiasing
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

  Scenario: Visible insertion target
    When I hover over the blank Add note area at the end of a Dashboard column or People list
    Then its entire hitbox has a background slightly darker than the app background
    And its size and the surrounding task positions remain unchanged

  Scenario: Main and helper window close behavior is visible
    Then a single native window is titled "Glance"
    And while multiple windows are open their titles are "G - Main" and "G - Secondary"
    And the remaining main window returns to "Glance" when helpers close
    When I close a helper window
    Then only that helper closes after its pending saves finish
    When I close the main window
    Then a Yes/No warning explains that all Glance windows will close, with No selected by default
    And cancellation leaves every window open
    And confirmation closes all windows after their pending saves finish
    And any failed save cancels closing and preserves the unsaved text

  Scenario: Caret intent when moving vertically between tasks
    Given Dashboard or People with another task in the current column or person
    When Up or Down moves between tasks from a paragraph's end
    Then the caret lands at the destination paragraph's end
    When Up or Down moves between tasks from a paragraph's beginning
    Then the caret lands at the destination paragraph's beginning
    And an empty source paragraph counts as a beginning
    And a caret in the middle retains the nearest horizontal position on the destination's entering visual line
    And within-task navigation remains unchanged
    And arrow navigation never creates tasks

  Scenario: Subtle helper window appearance
    Given a helper window opened through New window
    Then only its top navigation bar has a slightly darker neutral background
    And a compact regular-weight Helper window label appears beside the version without a border or background
    And the label tooltip explains that closing this helper leaves other windows open
    And task backgrounds, text, navigation controls, and the native title bar remain available
    And the main window keeps its usual navigation appearance

  Scenario: Enter at the end of a title with existing subcontent
    Given a task has existing subcontent in Dashboard or People
    When I press Enter at the end of its title with no text selected
    Then an empty first subcontent bullet is inserted and receives the caret
    And the title and all existing subcontent and nesting are preserved
    And no separate task is created
    And an immediate Tab cannot indent the original task
    And title-only Enter and middle-title splitting retain their existing behavior

  Scenario: Newly generated recurrence ordering
    When a weekly or monthly occurrence is generated
    Then it is inserted before existing tasks on its scheduled day
    And weekday grouping is preserved
    And running generation again does not duplicate or reorder existing occurrences

  Scenario: Windows taskbar identity
    Then the process and its portable Glance shortcut share the stable Glance.Desktop AppUserModelID
    And executable display metadata is capitalized Glance
    And opening the app on its destination PC creates or refreshes Glance.lnk beside the executable
    And Windows taskbar grouping and monitor preferences are not changed by Glance

  Scenario: People scrollbar and editor typography
    Then the People task scrolling viewport extends to the right window edge
    And the compact task content width is retained inside that viewport
    And plain task titles and subcontent share font family, size, weight, style, line height, letter spacing, and color
    And explicit rich-text marks, links and completed-task styling remain meaningful

  Scenario: Assigning tasks to remaining days of this week
    Given today is Wednesday
    Then This week shows compact Wednesday through Sunday headers even with no tasks
    And Monday and Tuesday headers appear only if they still contain visible tasks
    And those existing tasks retain their scheduled dates
    When I use a remaining day's plus button
    Then a task is created on that date and its title receives focus
    When I move a task using the Mon-Sun picker or drop it onto an upcoming day header
    Then it is scheduled for that date and the operation can be undone
    And the drop does not also run the surrounding column handler
    And past days are unavailable for new assignments
    And This week remains available when all its tasks have been removed

Feature: Nested subcontent deletion safety

  Scenario: Removing a nested empty bullet without losing surrounding content
    Given a task has a title and nested subcontent with parents and siblings
    When I select a nested line's text and delete it
    And I press Backspace again in its empty bullet
    Then only that empty item and its now-empty list wrapper are removed
    And the title, parent text, siblings, descendants outside that item, and rich marks remain
    And the change is saved and can be undone and redone
    And the behavior is the same in Dashboard and People

  Scenario: Empty nested lists do not trigger whole-task content clearing
    Given an empty item is the only child of a nested list
    When I press Backspace, Delete, or Enter there
    Then content outside that nested item is preserved
    And nested Enter does not trigger creation of a separate task
    And clearing a parent line does not remove its populated descendants

  Scenario: Noncanonical content is preserved when appending a list
    Given imported or pasted subcontent starts with plain paragraphs instead of a list
    When Enter appends a new list item
    Then all existing blocks remain intact

  Scenario: Compact weekday choices within the category menu
    When I open the task Move menu
    Then Mo Tu We Th Fr Sa Su appear directly below This week and before Next week
    And a second Mo Tu We Th Fr Sa Su row appears directly below Next week
    And next-week dates include Saturday and Sunday and remain enabled
    And the day row has no separate heading
    And tooltips and accessible labels identify full weekdays and dates

  Scenario: Creating an adjacent task retains its scheduled date
    Given I edit a task assigned to a specific day
    When Enter creates a new task below it
    Then the new task is assigned to the same day and receives focus
    And splitting a title or promoting subcontent also retains that date
    And Undo and Redo retain the assigned date
    And independent additions and explicit category moves keep their existing date defaults

  Scenario: Backspace removes an empty bullet and focuses backward
    Given an empty nested subcontent bullet has a preceding line
    When I remove that bullet with Backspace
    Then the caret is at the end of the preceding line, including a parent or nested sibling
    And unrelated content is preserved
    And if no preceding line exists the caret uses the first remaining text position

  Scenario: Disabled buttons do not respond visually to hover
    Given any app button is disabled, including a past weekday in the Move menu
    When I hover over it
    Then its background and other hover styling remain unchanged
    And its disabled appearance and tooltip remain available

Feature: Safe repeated Tab restructuring

  Scenario: Tab merges use acknowledged saves from the current window
    Given a task contains leading and trailing blank bullets around populated subcontent
    And it is followed by several empty tasks
    And an editor autosave is pending or in progress
    When I repeatedly press Tab in the following empty task titles
    Then the tasks become new blank subcontent lines in order
    And existing blank lines and populated content remain intact
    And the caret focuses each newly appended line
    And no conflict is generated by an older revision from this same window
    And the behavior is identical in Dashboard and People

  Scenario: Adjacent restructuring preserves structure and task identity
    When Tab requests arrive before an earlier Tab merge completes
    Then they are processed sequentially using current visible neighbours
    And Tab cannot merge across Dashboard columns
    And Undo restores the original tasks without recreating duplicates
    And Redo restores the merged content including blank lines
    And Shift+Tab preserves blank siblings before and after a promoted item
    And focusing elsewhere does not delete blank-only subcontent

  Scenario: Real external conflicts still preserve local edits
    Given another writer has saved a newer revision
    When my pending edit cannot be saved
    Then Tab restructuring stops without modifying either saved task
    And my local text remains available with the conflict notice
    And no fallback blank subcontent is created for a blocked merge


Feature: Compact column and selection controls

  Scenario: Readable weekday headings
    Given I am viewing This week in Dashboard
    Then its day headings use Monday through Sunday in full
    And the Move picker keeps compact Mo through Su buttons

  Scenario: Multi-selection menus
    Given I open a task's Send a copy menu or a person's Tags menu
    When I click an option's name or checkbox
    Then only that checkbox is toggled
    And the menu stays open for additional selections
    When I click outside the menu or press Escape
    Then the menu closes
    And Dashboard background panning never captures clicks inside task menus

  Scenario: Safe promotion and structural history
    Given a task has rich subcontent including nested items, images and intentional empty lines
    When I promote an outer item with Shift+Tab
    Then its destination is saved before the original is shortened
    And failures preserve the complete original and report the error
    And repeated promotion is blocked while that operation is pending
    And later typing is not replaced by an old snapshot
    And a successful promotion has one structural Undo and Redo in Dashboard and People
    And Undo or Redo restores retained destinations before removing copies or sources
    And fresh expected documents and revision guards protect other-window edits
    And failed or partially applied history stays available to retry without losing text
    And normal task deletion also settles saves and checks the selected revision
    And an image-only title does not count as an empty task

  Scenario: Plan all days of next week
    Given I open the Dashboard
    Then Next week remains available even when it has no tasks
    And its column has Monday through Sunday headings with their dates
    And each day accepts additions and whole-task drops
    And Enter and splits within a task inherit its scheduled day
    And navigation follows the visible weekday order
    And the category Move dropdown offers compact Mo-Su buttons directly below Next week
    And the column maximize icon has two arrows pointing towards opposite corners
    And its restore icon has two arrows pointing inward

  Scenario: Empty weekday drop targets show their insertion position
    Given a current or next-week day contains no tasks
    When I drag a whole task over that day
    Then a red insertion line appears below its weekday header without moving the layout
    And leaving the day, dropping or cancelling removes the line
    And ordinary text and image drags do not show a task insertion line
    And moving between header children does not dismiss the line

  Scenario: Spacing between weekday headings belongs to the preceding day
    Given Saturday and Sunday are empty planning days
    When I drag a whole task below Saturday and above the Sunday heading text
    Then Saturday's red insertion line remains visible across the entire blank space
    And dropping there schedules the task for Saturday, never the column's default Monday
    And Sunday's drop area begins at the Sunday heading text
    And this applies to every adjacent weekday in This week and Next week
    And dropping on task rows retains their before-or-after reordering behavior

  Scenario: Postpone dated work until the week after next
    When I move a task to In two weeks
    Then it is scheduled for Monday of the week after next
    And it appears in an In two weeks column immediately after Next week
    And its text and complete nested subcontent remain unchanged
    And the operation can be undone and redone
    And next week the same task appears in Next week without changing its stored date
    And the following week the same task appears in This week
    And an empty In two weeks column is omitted like other optional categories

Feature: Compact navigation and People usability

  Scenario: Navigate directly to the four main tabs
    Then the tab order is Dashboard, People, Search, History
    And Alt+1, Alt+2, Alt+3 and Alt+4 select those tabs respectively
    And the same shortcuts work while editing task text
    And Ctrl+1 through Ctrl+6, AltGr, modified keys and composition remain unaffected
    And tab tooltips and accessible shortcut labels describe the combinations
    And recovery mode still prevents navigation into editable views

  Scenario: Search receives focus when selected
    When I select Search by clicking or Alt+3
    Then its search field receives focus without clearing the saved query or results
    And selecting Search again focuses the field again

  Scenario: Filter many people without switching the open notes
    Given a person's notes are open
    When I type part of a name into Find person
    Then only matching active person tabs remain visible, ignoring letter case
    And the current person's notes stay open until I select a match
    And Enter selects the first matching person
    And Escape or clearing the field restores all active names
    And no matches has an explicit empty message without creating or losing notes

  Scenario: People notes show their creation date
    Given a current or previously saved People note has a recorded createdAt timestamp
    When its task action bar is visible
    Then it displays that creation date in local YYYY-MM-DD format
    And the date does not reserve extra row height

  Scenario: Pending-save feedback cannot change title wrapping
    When a task becomes dirty, starts saving or finishes saving
    Then the pending-save dot floats in a fixed small gutter
    And title width and line wrapping remain identical between those states
    And the dot cannot cover text or intercept clicks

  Scenario: Send copies directly between people
    Given a task with nested content exists under Alice
    When I send it to Bob or a tag group
    Then independent copies appear under the other active recipients without passing through Dashboard
    And the source remains intact with the same dismissible sent marker and destination list as Dashboard
    And each copy identifies Alice as its source
    And tag membership does not create a duplicate for Alice or duplicate recipients
    And pending edits are saved before copying

  Scenario: Every Glance window shares its pinned taskbar identity
    When a main or secondary native window is created
    Then its explicit window AppUserModelID matches the process and Glance shortcut
    And the identity property is cleared on a successful close
    And Windows controls the final taskbar grouping and placement

  Scenario: Retired report feature preserves old data
    Then Status Updates and its report integrations, endpoints, Office exports and marker controls are absent
    And Ctrl+7 no longer adds report markers
    And existing database columns, stored report files and rich-text metadata are preserved
    And backups, restores and portable exports can still carry that legacy data


Feature: Reliable compact task menus and private subcontent

  Scenario: Removing a leading blank subcontent bullet
    Given a blank bullet is followed by another bullet with text, rich marks or nested descendants
    When I press Delete at the blank bullet or Backspace at the following bullet start
    Then only the blank bullet is removed
    And the caret remains at the following line start
    And other content and descendants remain intact
    And native Undo and Redo restore the exact hierarchy
    And selections and modified keys retain their normal behavior
    And an empty parent with populated descendants is not treated as a disposable empty bullet

  Scenario: Sending to Dashboard and people from one menu
    Given I am viewing a task in People
    When I open Send a copy
    Then Send to Dashboard is the first checkbox above the people and tag groups
    And I can select Dashboard and multiple people together
    And one explicit Send confirmation creates independent copies and audit records atomically
    And the source and its privacy flag are preserved
    And Dashboard tasks use the same menu without a Dashboard recipient
    And the Send confirmation stays visible outside the scrolling recipient list
    And a failed send retains the menu and selections for retry

  Scenario: External links cannot navigate the Glance window
    When I activate a hyperlink with Ctrl or Meta click in an editor, a normal click in read-only text, middle click, or Enter on a focused anchor
    Then its destination is captured before editor focus expands shortened URLs
    And that destination is opened through the validated external opener before focusing the task
    And the native anchor navigation is cancelled
    And failed external activation reports an error without internal navigation
    And plain editing clicks retain caret placement without navigating
    And URL or file drops on the app background cannot replace Glance

  Scenario: Marking a task's subcontent private
    Given I mark a task Private subcontent using its focus-only task controls
    Then its title remains visible
    And the flag is saved without changing the title or subcontent
    And Dashboard, People, Search and History conceal its body while unfocused and privacy is on
    And the hidden body cannot receive pointer, keyboard or accessibility focus
    And concealment preserves row height and does not make tasks jump
    When I focus that task or deliberately activate its hidden-body placeholder
    Then its subcontent is shown
    When task focus leaves or Glance loses focus
    Then its body is concealed again
    And native window focus restoration alone does not reveal it

  Scenario: Global privacy and preservation
    Given every new Glance window starts with global privacy on
    When I turn privacy off using the top-bar lock button
    Then every flagged task's subcontent is revealed in that window
    When I turn it on again
    Then only the focused flagged task may remain revealed
    And unflagged tasks are unaffected
    And sent copies, recurring instances, whole-task clipboard payloads and promoted or split tasks retain privacy
    And combining tasks retains privacy if either source was private
    And Undo and Redo restore the corresponding flags
    And privacy is a screen presentation feature, not encryption or removal from backups, exports or the clipboard


Feature: Modern font-comparison styling without extra whitespace

  Scenario: Consistent restrained hierarchy
    Then Dashboard, People, Search, History and Settings use the shared IBM Plex Sans Notepad visual style
    And section and column headings use sentence case, normal letter spacing and genuine 600 weight at 7 CSS px
    And Settings subsection headings use 500 weight and keep their existing group indentation
    And ordinary and selected navigation controls retain 400 weight so selection cannot reflow them
    And selected navigation has a pale fill and inset bottom rule
    And thin neutral separators, small control radii and subtle menu shadows distinguish surfaces
    And History uses quiet gridlines and zero-height bars for zero-completion days
    And normal text and links remain neutral while semantic colors retain their meaning

  Scenario: Compact stable task geometry
    Given I focus a task in any task view
    Then a 2px left rail connects to the 1px full-width action-bar line
    And neither decoration adds layout borders or moves task text
    And the ownership indication retains its existing focus, window-blur and dragging rules
    And visible checkboxes are 10px inside their unchanged 12px hit areas and centered on the first title line
    And all task lists share 1px vertical row padding and 12px list indentation per level
    And font size, line height, column widths, task gaps and toolbar hit targets do not increase
    And narrow windows have no extra content padding or enlarged navigation gaps
    And disabled controls retain their appearance on hover
